using UnityEngine;

namespace ZeroSector.Campaign
{
    [ExecuteAlways,RequireComponent(typeof(TextMesh))]
    public sealed class CampaignWorldSign : MonoBehaviour
    {
        TextMesh text;
        Renderer surface;
        Texture atlas;
        MaterialPropertyBlock properties;
        void OnEnable()
        {
            properties=new MaterialPropertyBlock();
            text=GetComponent<TextMesh>(); surface=GetComponent<Renderer>(); atlas=null;
            if(text.font) text.font.RequestCharactersInTexture(text.text,text.fontSize,text.fontStyle);
            LateUpdate();
        }
        void LateUpdate()
        {
            if(!text || !surface || !text.font || !text.font.material) return;
            Texture next=text.font.material.mainTexture;
            if(!next) return;
            if(atlas==next) return;
            atlas=next; surface.GetPropertyBlock(properties); properties.SetTexture("_MainTex",atlas); surface.SetPropertyBlock(properties);
        }
    }
}
