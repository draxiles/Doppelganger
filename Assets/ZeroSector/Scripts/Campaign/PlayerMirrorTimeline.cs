using System.Collections.Generic;
using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class PlayerMirrorTimeline
    {
        public const double Delay = .5;

        public readonly struct Pose
        {
            public readonly Vector3 Displacement, Facing, Look;
            public readonly bool Crouched, Grounded, Sprinting;

            public Pose(Vector3 displacement,Vector3 facing,Vector3 look,bool crouched,bool grounded,bool sprinting)
            {
                Displacement=displacement;
                Facing=facing;
                Look=look;
                Crouched=crouched;
                Grounded=grounded;
                Sprinting=sprinting;
            }
        }

        public readonly struct Shot
        {
            public readonly Vector3 Target;
            public readonly float Damage;
            public readonly double FiredAt;

            public Shot(Vector3 target,float damage,double firedAt)
            {
                Target=target;
                Damage=damage;
                FiredAt=firedAt;
            }
        }

        readonly Queue<(double when,Pose value)> poses=new();
        readonly Queue<Shot> shots=new();
        public double Now { get; private set; }

        public void Advance(float dt) { Now+=dt; }
        public void ClearPending() { poses.Clear(); shots.Clear(); }
        public void RecordPose(Pose pose) { poses.Enqueue((Now,pose)); }
        public void RecordShot(Vector3 target,float damage) { shots.Enqueue(new Shot(target,damage,Now)); }

        public bool TakePose(out Pose pose)
        {
            if(poses.Count>0 && Now>=poses.Peek().when+Delay)
            {
                pose=poses.Dequeue().value;
                return true;
            }
            pose=default;
            return false;
        }

        public bool PeekShot(out Shot shot) => shots.TryPeek(out shot);

        public bool TakeShot(out Shot shot)
        {
            if(!PeekShot(out shot) || Now<shot.FiredAt+Delay) return false;
            shots.Dequeue();
            return true;
        }

        // Reflect across the transverse plane of the core, keeping world up and sideways motion.
        public static Vector3 Reflect(Vector3 vector) => new(vector.x,vector.y,-vector.z);
    }
}
