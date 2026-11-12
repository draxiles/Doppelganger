using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class HeadViewTracking
    {
        const float FreshFor = .28f;
        const float CenterTolerance = 3f;
        const int CenterSamples = 6;
        const float CenterSeconds = .23f;
        const float MaxYaw = 95f;
        const float MaxPitch = 52f;

        Vector2 center, sample, centerTotal, candidate;
        int centerCount;
        float lastSeen = float.NegativeInfinity;
        float centerStarted, candidateTime = float.NegativeInfinity;

        public Vector2 Offset { get; private set; }
        public float Sensitivity { get; set; }=1f;
        public bool IsCalibrated { get; private set; }
        public bool IsTracking(float now) => now - lastSeen <= FreshFor;

        public void Observe(Vector2 angles, float now)
        {
            if(!(Mathf.Abs(angles.x)<=65 && Mathf.Abs(angles.y)<=55) || now<=lastSeen) return;

            if(!IsCalibrated)
            {
                if(Mathf.Abs(angles.x)>24f || Mathf.Abs(angles.y)>30f)
                {
                    centerCount=0;
                    centerTotal=Vector2.zero;
                    return;
                }
                if(centerCount==0 || !IsTracking(now) || Vector2.Distance(angles,centerTotal/centerCount)>CenterTolerance)
                {
                    centerCount=0;
                    centerTotal=Vector2.zero;
                    centerStarted=now;
                }
                centerTotal+=angles;
                if(++centerCount>=CenterSamples && now-centerStarted>=CenterSeconds)
                {
                    center=centerTotal/centerCount;
                    IsCalibrated=true;
                }
            }
            else if(IsTracking(now) && Vector2.Distance(sample,angles)>27f)
            {
                // Confirm sudden large changes on the next image before turning the view.
                if(now-candidateTime>.13f || Vector2.Distance(candidate,angles)>9f)
                {
                    candidate=angles;
                    candidateTime=now;
                    return;
                }
            }

            candidateTime=float.NegativeInfinity;
            sample=angles;
            lastSeen=now;
        }

        public void Tick(float now, float dt)
        {
            Vector2 target=Vector2.zero;
            bool tracking=IsCalibrated && IsTracking(now);
            if(tracking)
            {
                // Gentle near center, with a game quarter-turn at about 26 degrees of physical yaw.
                target.x=Mathf.Clamp(Scale(Mathf.DeltaAngle(center.x,sample.x),1.7f,2f,.074f)*Sensitivity,-MaxYaw,MaxYaw);
                target.y=Mathf.Clamp(Scale(Mathf.DeltaAngle(center.y,sample.y),1.5f,1.8f,.045f)*Sensitivity,-MaxPitch,MaxPitch);
            }
            float error=Vector2.Distance(Offset,target);
            float rate=tracking ? Mathf.Lerp(15f,32f,Mathf.InverseLerp(3f,35f,error)) : 6f;
            Offset=Vector2.Lerp(Offset,target,1f-Mathf.Exp(-rate*Mathf.Max(0,dt)));
        }

        static float Scale(float angle,float deadZone,float sensitivity,float acceleration)
        {
            float amount=Mathf.Max(0,Mathf.Abs(angle)-deadZone);
            return Mathf.Sign(angle)*amount*(sensitivity+amount*acceleration);
        }

        public void LoseFace()
        {
            lastSeen=float.NegativeInfinity;
            candidateTime=float.NegativeInfinity;
            centerCount=0;
            centerTotal=Vector2.zero;
        }

        public void Suspend()
        {
            LoseFace();
            Offset=Vector2.zero;
        }

        public void Recenter()
        {
            Suspend();
            IsCalibrated=false;
        }
    }
}
