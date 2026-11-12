using UnityEngine;

namespace ZeroSector.Campaign
{
    /// <summary>Manual correction of the ship's attitude, seen through the forward windows.</summary>
    public sealed class CampaignFlight
    {
        readonly CampaignGame game;
        Transform planet;
        Vector3 planetPosition, eyePosition, velocity;
        Quaternion eyeRotation;
        float stability;
        public bool Active { get; private set; }
        public Vector3 Attitude { get; private set; }
        public Vector3 Error => -Attitude;
        public float Stability => Mathf.Clamp01(stability/1.2f);
        public bool Ready => stability>=1.2f;
        public CampaignFlight(CampaignGame game) { this.game=game; }

        public void Begin()
        {
            if(Active) return;
            Active=true; Attitude=new Vector3(-13,17,-12); velocity=Vector3.zero; stability=0;
            var eye=game.eyes.transform;
            eyePosition=eye.localPosition; eyeRotation=eye.localRotation;
            planet=GameObject.Find("Earth / satellite surface, clouds and atmosphere")?.transform;
            if(planet) planetPosition=planet.position;
            game.presentation.SetHolstered(true);
            game.presentation.ExternalCamera=true;
            Render();
        }
        public void Tick(Vector3 input,float dt)
        {
            if(!Active || dt<=0) return;
            velocity=Vector3.MoveTowards(velocity,input*21,dt*120);
            Attitude=Vector3.Max(Vector3.one*-35,Vector3.Min(Vector3.one*35,Attitude+velocity*dt));
            bool aligned=Mathf.Max(Mathf.Abs(Attitude.x),Mathf.Abs(Attitude.y),Mathf.Abs(Attitude.z))<3.5f && velocity.sqrMagnitude<5;
            stability=aligned ? Mathf.Min(1.2f,stability+dt) : 0;
            Render();
        }
        void Render()
        {
            var eye=game.eyes.transform;
            eye.SetPositionAndRotation(new Vector3(0,2.05f,9.3f),Quaternion.Euler(3+Attitude.x*.12f,Attitude.y*.09f,-Attitude.z*.25f));
            if(planet) planet.position=eye.position+Quaternion.Inverse(Quaternion.Euler(Attitude))*(planetPosition-eye.position);
        }
        public void End()
        {
            if(!Active) return;
            Active=false;
            var eye=game.eyes.transform;
            eye.localPosition=eyePosition; eye.localRotation=eyeRotation;
            if(planet) planet.position=planetPosition;
            game.presentation.ExternalCamera=false;
            game.presentation.SetHolstered(false);
        }
    }
}
