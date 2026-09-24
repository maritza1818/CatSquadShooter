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


def empty_packet():
    return {"t": time.time(), "pose": 0, "handL": 0, "handR": 0,
            "openL": 1.0, "openR": 1.0, "rollL": 0.0, "rollR": 0.0}


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
            if hand_result.hand_world_landmarks and i < len(hand_result.hand_world_landmarks):
                pkt["roll" + side] = round(hand_roll(hand_result.hand_world_landmarks[i]), 1)
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


def run_demo(args, sock, target):
    """Sin cámara: simula a un niño moviendo los brazos (útil para probar Unity)."""
    print(f"Modo DEMO: enviando brazos simulados a {target[0]}:{target[1]} (Ctrl+C para salir)")
    t0 = time.time()
    period = 1.0 / 30.0
    while True:
        t = time.time() - t0
        ls, rs = [0.40, 0.40, 0.99], [0.60, 0.40, 0.99]   # espejo: el hombro izquierdo se ve a la izquierda
        # mano izquierda (panel): pasea y cada 8 s sube y se mantiene (palanca);
        # mano derecha (brazo): alcanza en arcos cada vez más amplios
        lift = 1.0 if (t % 8.0) > 5.0 else 0.0
        lw = [0.36 + 0.06 * math.sin(t * 0.7), 0.72 - 0.28 * lift, 0.95]
        reach = 0.5 + 0.5 * math.sin(t * 0.9)
        ang = t * 0.6
        rw = [0.64 + 0.25 * reach * math.cos(ang), 0.72 - 0.30 * reach * (0.5 + 0.5 * math.sin(ang)), 0.95]
        pkt = {"t": time.time(), "pose": 1, "ls": ls, "rs": rs,
               "le": [0.36, 0.56, 0.9], "re": [0.64, 0.56, 0.9], "lw": lw, "rw": rw,
               "lh": [0.43, 0.85, 0.3], "rh": [0.57, 0.85, 0.3],
               "handL": 1, "handR": 1, "openL": 1.0,
               "openR": 0.1 if reach > 0.85 else 1.0,
               "rollL": 0.0, "rollR": 70.0 if (t % 12.0) > 9.0 else 0.0}
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
