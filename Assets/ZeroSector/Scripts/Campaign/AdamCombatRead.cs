using UnityEngine;

namespace ZeroSector.Campaign
{
    /// <summary>Short memory of actions ADAM can see. It is reset for each encounter.</summary>
    public sealed class AdamCombatRead
    {
        float firePressure, holdTime, strafeTrend, strafeTime;
        Vector3 lastVelocity;

        public bool RepeatedFire => firePressure>=1.5f;
        public bool HoldingCover => holdTime>=.8f;
        public bool PredictingStrafe => strafeTime>=.28f && Mathf.Abs(strafeTrend)>.8f;
        public float FlankSide => strafeTrend>=0 ? 1 : -1;

        public void RegisterShot() { firePressure=Mathf.Min(4,firePressure+1); }

        public void Observe(float dt,bool visible,Vector3 planarVelocity,Vector3 side,bool crouching)
        {
            firePressure=Mathf.Max(0,firePressure-dt*.42f);
            if(!visible)
            {
                lastVelocity=Vector3.zero;
                holdTime=Mathf.MoveTowards(holdTime,0,dt*1.8f);
                strafeTime=0;
                return;
            }

            lastVelocity=Vector3.Lerp(lastVelocity,planarVelocity,1-Mathf.Exp(-9*dt));
            float lateral=Vector3.Dot(planarVelocity,side);
            bool consistent=Mathf.Abs(lateral)>1.1f && (Mathf.Abs(strafeTrend)<.3f || lateral*strafeTrend>0);
            strafeTime=consistent ? Mathf.Min(1,strafeTime+dt) : Mathf.Max(0,strafeTime-dt*2);
            strafeTrend=Mathf.Lerp(strafeTrend,lateral,1-Mathf.Exp(-4*dt));
            holdTime=planarVelocity.sqrMagnitude<.45f ? Mathf.Min(2,holdTime+dt*(crouching ? 1.5f : 1)) : Mathf.Max(0,holdTime-dt*2.5f);
        }

        public Vector3 AimAt(Vector3 position) => PredictingStrafe ? position+Vector3.ClampMagnitude(lastVelocity*.24f,1.15f) : position;
    }
}
