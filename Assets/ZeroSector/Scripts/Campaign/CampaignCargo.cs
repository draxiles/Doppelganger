using UnityEngine;

namespace ZeroSector.Campaign
{
    /// <summary>One physical emergency battery. Its home stays available if a loose battery is lost.</summary>
    public sealed class CampaignCargo : MonoBehaviour
    {
        public const string Id="fuse";
        public bool Held { get; private set; }
        public CampaignInteractable Interaction { get; private set; }
        CampaignGame game;
        Rigidbody body;
        Collider shell;
        Vector3 dock;
        Quaternion dockRotation;
        float returnTimer,elapsed;

        public void Initialize(CampaignGame owner,bool restoreHeld,bool installed)
        {
            game=owner; dock=transform.position; dockRotation=transform.rotation;
            body=GetComponent<Rigidbody>(); shell=GetComponent<Collider>(); Interaction=GetComponent<CampaignInteractable>();
            if(installed) Install();
            else if(restoreHeld) PickUp();
        }
        public void PickUp()
        {
            returnTimer=0; elapsed=0;
            if(!body.isKinematic) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; }
            body.isKinematic=true; shell.enabled=false;
            Held=true; transform.SetParent(game.eyes.transform,false); HoldPose();
            game.presentation.SetHolstered(true);
        }
        void HoldPose()
        {
            transform.localPosition=new Vector3(.25f,-.29f+Mathf.Sin(elapsed*8)*.008f,.69f);
            transform.localRotation=Quaternion.Euler(-12,-23,-9);
        }
        public bool Drop()
        {
            if(!Held) return false;
            var eye=game.eyes.transform;
            Vector3 direction=Vector3.ProjectOnPlane(eye.forward,Vector3.up).normalized;
            Vector3 origin=eye.position+Vector3.down*.37f;
            float distance=.92f;
            foreach(var hit in Physics.SphereCastAll(origin,.24f,direction,distance,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider!=game.player && !hit.transform.IsChildOf(game.player.transform))
                    distance=Mathf.Min(distance,hit.distance-.09f);
            if(distance<.56f) return false;
            Held=false; transform.SetParent(null,true);
            transform.SetPositionAndRotation(origin+direction*distance,Quaternion.LookRotation(direction));
            shell.enabled=true; Physics.IgnoreCollision(shell,game.player,true);
            body.isKinematic=false; body.AddForce(direction*.65f,ForceMode.VelocityChange);
            returnTimer=25; game.presentation.SetHolstered(false); return true;
        }
        public void Install()
        {
            Held=false; transform.SetParent(null,true); body.isKinematic=true; shell.enabled=false;
            game.presentation.SetHolstered(false); gameObject.SetActive(false);
        }
        public void Tick(float dt)
        {
            if(Held) { elapsed+=dt; HoldPose(); return; }
            if(returnTimer<=0) return;
            returnTimer-=dt;
            if(returnTimer>0 && transform.position.y>=-.7f) return;
            body.isKinematic=true;
            transform.SetPositionAndRotation(dock,dockRotation);
            game.Notify("Сервисный захват вернул аккумулятор в стойку.");
        }
    }
}
