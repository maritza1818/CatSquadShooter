"""
Cat Squad: Rescate Profundo - Puente de visión (webcam -> Unity)
=================================================================

Capa "Procesamiento -> Estimación de pose" de la arquitectura:
  Webcam -> MediaPipe (pose + manos) -> JSON por UDP (127.0.0.1:5065) -> Unity

Python solo PERCIBE (dónde están hombros, codos, muñecas, caderas y si la mano
está abierta). Toda la INTERPRETACIÓN (alcance normalizado, palanca, compensación
de tronco, calibración) la hace Unity en WebcamPoseInputProvider.cs, para que el
terapeuta pueda ajustarla desde el juego.

Uso:
    pip install -r requirements.txt
    python catsquad_pose_bridge.py              # webcam 0 con ventana de vista previa
    python catsquad_pose_bridge.py --camera 1   # otra cámara
    python catsquad_pose_bridge.py --demo       # SIN cámara: brazos simulados (para probar Unity)

Coordenadas enviadas: normalizadas 0..1 en ESPEJO (como se ve uno en un espejo),
y hacia abajo. Las etiquetas izquierda/derecha son ANATÓMICAS (lado del niño).
"""
import argparse
import json
import math
import os
import socket
import sys
import time
import urllib.request

POSE_URL = ("https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
            "pose_landmarker_{variant}/float16/latest/pose_landmarker_{variant}.task")
HAND_URL = ("https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
            "hand_landmarker/float16/latest/hand_landmarker.task")

# Índices de MediaPipe Pose
L_SHOULDER, R_SHOULDER = 11, 12
L_ELBOW, R_ELBOW = 13, 14
L_WRIST, R_WRIST = 15, 16
L_HIP, R_HIP = 23, 24


def ensure_model(url, folder):
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, url.rsplit("/", 1)[-1])
    if not os.path.exists(path):
        print(f"Descargando modelo {os.path.basename(path)} ...")
        urllib.request.urlretrieve(url, path)
    return path


def point(lm, mirror=True):
    """[x, y, visibilidad] en coordenadas espejo."""
    x = 1.0 - lm.x if mirror else lm.x
    vis = getattr(lm, "visibility", None)
    if vis is None:
        vis = 1.0
    return [round(x, 4), round(lm.y, 4), round(float(vis), 3)]


def hand_openness(landmarks, aspect):
    """0 = puño, 1 = mano abierta. Distancia media de las puntas a la muñeca / tamaño de la palma."""
    def d(a, b):
        return math.hypot((a.x - b.x) * aspect, a.y - b.y)
    wrist = landmarks[0]
    palm = d(wrist, landmarks[9]) + 1e-6
    tips = [landmarks[i] for i in (8, 12, 16, 20)]
    ratio = sum(d(t, wrist) for t in tips) / (4 * palm)
    return max(0.0, min(1.0, (ratio - 1.25) / 0.7))


def hand_roll(world_landmarks):
    """Rotación de antebrazo (pronación/supinación) estimada con el vector índice->meñique en 3D."""
    a, b = world_landmarks[5], world_landmarks[17]
    return math.degrees(math.atan2(b.z - a.z, b.x - a.x))


def _sub(a, b):
    return (a.x - b.x, a.y - b.y, a.z - b.z)


def _dot(u, v):
    return u[0] * v[0] + u[1] * v[1] + u[2] * v[2]


def _cross(u, v):
    return (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])


def _norm(u):
    return math.sqrt(_dot(u, u)) + 1e-9


def hand_shape(world, side):
    """
    Forma de la mano en 3D (landmarks "world" de MediaPipe: metros, x a la derecha, y hacia ABAJO,
    z hacia la cámara negativo). Solo MIDE; Unity decide qué gesto es con las plantillas calibradas.
      n      normal que sale de la PALMA (espejada en x como el resto de coordenadas)
      ext    extensión de cada dedo [pulgar, índice, medio, anular, meñique] (~1 doblado, ~2 estirado)
      open3  apertura 0..1 con los cuatro dedos largos (en 3D: no le afecta que los dedos apunten a la cámara)
      tlift  cuánto se separa el pulgar de la palma hacia el dorso (palma abajo: "levantar el pulgar")
      tup    cuánto sube la punta del pulgar respecto de su base (mano vertical: "pulgar arriba")
    """
    w = world
    palm = _norm(_sub(w[9], w[0]))
    vi, vp = _sub(w[5], w[0]), _sub(w[17], w[0])
    n = _cross(vi, vp)
    if side == "L":            # la mano izquierda es el espejo de la derecha: se invierte el producto cruz
        n = (-n[0], -n[1], -n[2])
    ln = _norm(n)
    n = (n[0] / ln, n[1] / ln, n[2] / ln)
    ext = []
    for mcp, tip in ((2, 4), (5, 8), (9, 12), (13, 16), (17, 20)):
        ext.append(_norm(_sub(w[tip], w[0])) / (_norm(_sub(w[mcp], w[0])) + 1e-6))
    open3 = sum(ext[1:]) / 4.0
    open3 = max(0.0, min(1.0, (open3 - 1.25) / 0.65))
    back = (-n[0], -n[1], -n[2])                      # hacia el dorso de la mano
    tlift = _dot(_sub(w[4], w[2]), back) / palm
    tup = (w[2].y - w[4].y) / palm                    # y hacia abajo: positivo = la punta está más arriba
    return {
        "n": [round(-n[0], 3), round(n[1], 3), round(n[2], 3)],   # x espejada
        "ext": [round(e, 3) for e in ext],
        "open3": round(open3, 3),
        "tlift": round(tlift, 3),
        "tup": round(tup, 3),
    }


def empty_packet():
    return {"t": time.time(), "pose": 0, "handL": 0, "handR": 0,
            "openL": 1.0, "openR": 1.0, "rollL": 0.0, "rollR": 0.0,
            "open3L": 1.0, "open3R": 1.0, "tliftL": 0.0, "tliftR": 0.0, "tupL": 0.0, "tupR": 0.0}


def build_packet(pose_result, hand_result, aspect):
    pkt = empty_packet()
    if not pose_result.pose_landmarks:
        return pkt
    lms = pose_result.pose_landmarks[0]
    pkt["pose"] = 1
    for key, idx in (("ls", L_SHOULDER), ("rs", R_SHOULDER), ("le", L_ELBOW), ("re", R_ELBOW),
                     ("lw", L_WRIST), ("rw", R_WRIST), ("lh", L_HIP), ("rh", R_HIP)):
        pkt[key] = point(lms[idx])

    # Asociar cada mano detectada a la muñeca de pose más cercana (más robusto que la etiqueta de lateralidad)
    if hand_result is not None and hand_result.hand_landmarks:
        wl, wr = lms[L_WRIST], lms[R_WRIST]
        for i, hand in enumerate(hand_result.hand_landmarks):
            hw = hand[0]
            dl = math.hypot(hw.x - wl.x, hw.y - wl.y)
            dr = math.hypot(hw.x - wr.x, hw.y - wr.y)
            side = "L" if dl < dr else "R"
            if min(dl, dr) > 0.2:
                continue
            pkt["hand" + side] = 1
            pkt["open" + side] = round(hand_openness(hand, aspect), 3)
            # centro de la mano (nudillo del medio) en coordenadas espejo, para "juntar las manos"
            pkt["hc" + side] = [round(1.0 - hand[9].x, 4), round(hand[9].y, 4)]
            if hand_result.hand_world_landmarks and i < len(hand_result.hand_world_landmarks):
                world = hand_result.hand_world_landmarks[i]
                pkt["roll" + side] = round(hand_roll(world), 1)
                shape = hand_shape(world, side)
                pkt["n" + side] = shape["n"]
                pkt["ext" + side] = shape["ext"]
                pkt["open3" + side] = shape["open3"]
                pkt["tlift" + side] = shape["tlift"]
                pkt["tup" + side] = shape["tup"]
    return pkt


def run_camera(args, sock, target):
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision

    models = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models")
    pose_path = ensure_model(POSE_URL.format(variant=args.pose_model), models)
    hand_path = ensure_model(HAND_URL, models)

    pose = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=pose_path),
        running_mode=vision.RunningMode.VIDEO, num_poses=1,
        min_pose_detection_confidence=0.5, min_tracking_confidence=0.5))
    hands = None
    if not args.no_hands:
        hands = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=hand_path),
            running_mode=vision.RunningMode.VIDEO, num_hands=2,
            min_hand_detection_confidence=0.5, min_tracking_confidence=0.5))

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print("No se pudo abrir la cámara", args.camera)
        sys.exit(1)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 960)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 540)

    print(f"Enviando a {target[0]}:{target[1]}  (Q o ESC para salir)")
    t0 = time.time()
    last_ts = -1
    frames, fps_t, fps = 0, time.time(), 0.0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        h, w = frame.shape[:2]
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        ts = int((time.time() - t0) * 1000)
        if ts <= last_ts:
            ts = last_ts + 1
        last_ts = ts

        pose_res = pose.detect_for_video(image, ts)
        hand_res = hands.detect_for_video(image, ts) if hands else None
        pkt = build_packet(pose_res, hand_res, w / float(h))
        sock.sendto(json.dumps(pkt).encode("utf-8"), target)

        frames += 1
        if time.time() - fps_t >= 1.0:
            fps = frames / (time.time() - fps_t)
            frames, fps_t = 0, time.time()

        if not args.no_preview:
            view = cv2.flip(frame, 1)  # vista espejo
            for key, color in (("ls", (255, 200, 0)), ("rs", (255, 200, 0)), ("le", (0, 200, 255)), ("re", (0, 200, 255)),
                               ("lw", (0, 255, 0)), ("rw", (0, 0, 255)), ("lh", (200, 200, 200)), ("rh", (200, 200, 200))):
                if key in pkt and pkt[key][2] > 0.4:
                    cv2.circle(view, (int(pkt[key][0] * w), int(pkt[key][1] * h)), 8, color, -1)
            for side, key in (("L", "lw"), ("R", "rw")):
                if pkt.get("hand" + side) and key in pkt:
                    txt = f"{'cerrada' if pkt['open' + side] < 0.35 else 'abierta'} {pkt['roll' + side]:+.0f} grados"
                    if ("n" + side) in pkt:
                        nx, ny, _ = pkt["n" + side]
                        pose = "palma abajo" if ny > 0.6 else ("de canto" if abs(nx) > 0.6 else "")
                        txt += f"  {pose}  pulgar {pkt['tlift' + side]:+.1f}/{pkt['tup' + side]:+.1f}"
                    cv2.putText(view, txt, (int(pkt[key][0] * w) + 12, int(pkt[key][1] * h)), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            status = f"{fps:4.1f} fps  cuerpo: {'SI' if pkt['pose'] else 'no'}  (verde = mano izq., rojo = mano der.)"
            cv2.putText(view, status, (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            cv2.imshow("Cat Squad - vision", view)
            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord("q")):
                break
    cap.release()
    if not args.no_preview:
        cv2.destroyAllWindows()


# Formas de mano de referencia para el modo demo (las mismas que usa Unity por defecto)
DEMO_SHAPES = {
    "reposo":        dict(n=[0, 1, 0], open3=0.25, tlift=0.0, tup=0.0, ext=[1.3, 1.3, 1.3, 1.3, 1.3]),
    "pulgar":        dict(n=[0, 1, 0], open3=0.25, tlift=0.6, tup=0.5, ext=[1.9, 1.3, 1.3, 1.3, 1.3]),
    "abierta":       dict(n=[0, 1, 0], open3=0.9, tlift=0.1, tup=0.1, ext=[1.8, 1.9, 1.9, 1.9, 1.9]),
    "canto":         dict(n=[1, 0, 0], open3=0.15, tlift=0.0, tup=0.2, ext=[1.3, 1.1, 1.1, 1.1, 1.1]),
    "canto_pulgar":  dict(n=[1, 0, 0], open3=0.15, tlift=0.1, tup=0.55, ext=[1.9, 1.1, 1.1, 1.1, 1.1]),
    "puno":          dict(n=[0, 1, 0], open3=0.05, tlift=0.0, tup=0.0, ext=[1.2, 1.0, 1.0, 1.0, 1.0]),
}

# Guion del demo (segundos): qué hace cada mano. Izquierda = panel (timón), derecha = brazo.
DEMO_SCRIPT = [
    (4, "reposo", "reposo", "quieto: timón estacionario"),
    (4, "canto", "reposo", "mano de canto: AVANZA"),
    (2, "canto_pulgar", "reposo", "de canto + pulgar: RETROCEDE"),
    (3, "reposo_alta", "reposo", "mano levantada: SUBE (y palanca)"),
    (2, "abierta", "reposo", "mano abierta: BAJA"),
    (2, "reposo_der", "reposo", "mano a la derecha: GIRA"),
    (2, "juntas", "juntas", "manos juntas: abre/cierra el panel"),
    (1.5, "reposo", "pulgar", "pulgar derecho: selección a la derecha"),
    (1.5, "reposo", "reposo", ""),
    (1.5, "pulgar", "reposo", "pulgar izquierdo: selección a la izquierda"),
    (1.5, "reposo", "reposo", ""),
    (1.5, "pulgar", "pulgar", "ambos pulgares: CONFIRMAR"),
    (6, "reposo", "brazo", "el brazo alcanza y agarra"),
]


def demo_shape(name, side, t):
    base = dict(DEMO_SHAPES[name])
    n = list(base["n"])
    if name.startswith("canto") and side == "R":
        n[0] = -n[0]                     # de canto la palma mira al centro del cuerpo
    wob = 0.03 * math.sin(t * 7.0 + (0 if side == "L" else 1.3))
    return {"n" + side: [round(n[0] + wob, 3), round(n[1] - abs(wob), 3), round(n[2] + wob, 3)],
            "ext" + side: base["ext"], "open3" + side: round(base["open3"] + wob, 3),
            "tlift" + side: round(base["tlift"] + wob, 3), "tup" + side: round(base["tup"] + wob, 3)}


def run_demo(args, sock, target):
    """Sin cámara: simula a un niño haciendo los gestos acordados (útil para probar Unity)."""
    print(f"Modo DEMO: enviando gestos simulados a {target[0]}:{target[1]} (Ctrl+C para salir)")
    t0 = time.time()
    period = 1.0 / 30.0
    total = sum(d for d, *_ in DEMO_SCRIPT)
    last_label = None
    while True:
        t = time.time() - t0
        tt = t % total
        acc = 0.0
        for dur, left, right, label in DEMO_SCRIPT:
            if tt < acc + dur:
                break
            acc += dur
        if label and label != last_label:
            print("  ·", label)
        last_label = label
        ls, rs = [0.40, 0.40, 0.99], [0.60, 0.40, 0.99]   # espejo: el hombro izquierdo se ve a la izquierda
        lw = [0.36, 0.72, 0.95]
        rw = [0.64, 0.72, 0.95]
        if left == "reposo_alta":
            lw[1] = 0.46
        if left == "reposo_der":
            lw[0] = 0.47
        if left == "juntas":
            lw, rw = [0.48, 0.62, 0.95], [0.52, 0.62, 0.95]
        reach = 0.0
        if right == "brazo":
            reach = 0.5 + 0.5 * math.sin(t * 1.2)
            ang = t * 0.6
            rw = [0.64 + 0.25 * reach * math.cos(ang), 0.72 - 0.30 * reach * (0.5 + 0.5 * math.sin(ang)), 0.95]
        pkt = {"t": time.time(), "pose": 1, "ls": ls, "rs": rs,
               "le": [0.36, 0.56, 0.9], "re": [0.64, 0.56, 0.9], "lw": lw, "rw": rw,
               "lh": [0.43, 0.85, 0.3], "rh": [0.57, 0.85, 0.3],
               "handL": 1, "handR": 1, "openL": 0.6, "openR": 0.6,
               "rollL": 0.0, "rollR": 0.0,
               "hcL": [lw[0], lw[1] - 0.03], "hcR": [rw[0], rw[1] - 0.03]}
        lname = {"reposo_alta": "reposo", "reposo_der": "reposo", "juntas": "reposo"}.get(left, left)
        rname = {"juntas": "reposo", "brazo": "puno" if reach > 0.85 else "abierta"}.get(right, right)
        pkt.update(demo_shape(lname, "L", t))
        pkt.update(demo_shape(rname, "R", t))
        sock.sendto(json.dumps(pkt).encode("utf-8"), target)
        time.sleep(period)


def main():
    p = argparse.ArgumentParser(description="Puente webcam -> Unity para Cat Squad: Rescate Profundo")
    p.add_argument("--camera", type=int, default=0)
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=5065)
    p.add_argument("--pose-model", choices=["lite", "full", "heavy"], default="lite")
    p.add_argument("--no-hands", action="store_true", help="no detectar manos (el juego usa 'mantener quieto' para agarrar)")
    p.add_argument("--no-preview", action="store_true")
    p.add_argument("--demo", action="store_true", help="enviar movimientos simulados sin cámara")
    args = p.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    try:
        if args.demo:
            run_demo(args, sock, target)
        else:
            run_camera(args, sock, target)
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()


if __name__ == "__main__":
    main()
