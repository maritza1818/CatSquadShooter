using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum KittenState { Atrapado, Siguiendo, VolviendoAlMuelle, Rescatado }

    /// <summary>
    /// Gatito varado (fase 2). Al liberarlo, sigue al submarino en fila usando ARRIVE:
    /// su objetivo es un punto del rastro del submarino a cierta distancia detrás.
    /// Arrive (y no Seek) porque debe mantenerse en su lugar de la fila sin chocar
    /// ni adelantarse: frena al llegar a su hueco. Al emerger en el muelle, salta al
    /// muelle (ruta = Seek + Arrive) y queda a salvo para siempre.
    /// </summary>
    [RequireComponent(typeof(SteeringAgent))]
    public class RescuedKitten : MonoBehaviour
    {
        private static readonly List<RescuedKitten> followers = new List<RescuedKitten>();
        public static IReadOnlyList<RescuedKitten> Followers { get { return followers; } }

        public string kittenId = "gatito";
        public string kittenName = "Michi";

        [Header("Fila (Arrive)")]
        public float firstSlotDistance = 2.4f;
        public float slotSpacing = 1.5f;
        public float slowingRadius = 2.5f;
        public float verticalOffset = 0.2f;

        public KittenState State { get; private set; }
        public static event Action<RescuedKitten> AnyFreed;
        public static event Action<RescuedKitten> AnyRescued;

        private SteeringAgent agent;
        private readonly PathFollower dockPath = new PathFollower { waypointRadius = 0.8f, slowingRadius = 1.5f, stopRadius = 0.08f };
        private Vector3 seat;
        private Dock seatDock;
        private Vector3 seatLocal;

        private void Awake()
        {
            agent = GetComponent<SteeringAgent>();
            State = KittenState.Atrapado;
        }

        private void Start()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsKittenRescued(kittenId))
            {
                Dock dock = Dock.Instance;
                if (dock != null)
                {
                    agent.Teleport(dock.ReserveSeat());
                    seatDock = dock;
                    seatLocal = dock.transform.InverseTransformPoint(agent.Position);
                }
                State = KittenState.Rescatado;
            }
        }

        private void OnDisable()
        {
            followers.Remove(this);
        }

        public void Free()
        {
            if (State != KittenState.Atrapado) return;
            State = KittenState.Siguiendo;
            if (!followers.Contains(this)) followers.Add(this);
            if (AnyFreed != null) AnyFreed(this);
        }

        public void GoToDock(Dock dock)
        {
            if (State != KittenState.Siguiendo || dock == null) return;
            followers.Remove(this);
            State = KittenState.VolviendoAlMuelle;
            seat = dock.ReserveSeat();
            seatDock = dock;
            seatLocal = dock.transform.InverseTransformPoint(seat);
            List<Vector3> path = new List<Vector3>();
            if (dock.waterEntry != null) path.Add(dock.waterEntry.position);
            path.Add(seat + Vector3.up * 1.2f);
            path.Add(seat);
            dockPath.SetPath(path);
            if (GameManager.Instance != null) GameManager.Instance.MarkKittenRescued(kittenId);
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.kittenMeow : null, transform.position, 0.8f, 1.2f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;

            switch (State)
            {
                case KittenState.Atrapado:
                    agent.Brake(dt);
                    if (sub != null) agent.FaceTowards(sub.transform.position);
                    break;

                case KittenState.Rescatado:
                    // sentado en el muelle o en una balsa (que sube y baja con el agua del barrio)
                    if (seatDock != null) agent.Teleport(seatDock.transform.TransformPoint(seatLocal));
                    else agent.Brake(dt);
                    if (sub != null) agent.FaceTowards(sub.transform.position);
                    break;

                case KittenState.Siguiendo:
                    if (sub == null) { agent.Brake(dt); break; }
                    int slot = Mathf.Max(0, followers.IndexOf(this));
                    Vector3 target = sub.GetPointBehind(firstSlotDistance + slot * slotSpacing) + Vector3.up * verticalOffset;
                    if (agent.body.lockToPlane) target.z = agent.body.planeZ;
                    agent.ArriveAt(target, slowingRadius, 0.08f, dt);
                    break;

                case KittenState.VolviendoAlMuelle:
                    agent.FollowPath(dockPath, dt);
                    if (dockPath.Arrived(agent.Position, agent.Velocity, 0.4f))
                    {
                        State = KittenState.Rescatado;
                        if (AnyRescued != null) AnyRescued(this);
                    }
                    break;
            }
        }
    }
}
