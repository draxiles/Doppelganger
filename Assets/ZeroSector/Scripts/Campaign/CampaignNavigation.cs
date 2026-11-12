using System.Collections.Generic;
using UnityEngine;

namespace ZeroSector.Campaign
{
    // A navigation grid built from the actual level colliders. Tactical intent is chosen elsewhere.
    public sealed class CampaignNavigation
    {
        const float Cell=1;
        readonly Vector2 min;
        readonly int width,height;
        readonly bool[] walkable;
        readonly Collider[] nearby=new Collider[32];
        readonly float[] cost;
        readonly int[] previous;
        readonly bool[] closed;
        readonly List<int> open=new();
        public CampaignNavigation(Vector2 minimum,Vector2 maximum)
        {
            min=minimum; width=Mathf.Clamp(Mathf.CeilToInt((maximum.x-min.x)/Cell),1,100);
            height=Mathf.Clamp(Mathf.CeilToInt((maximum.y-min.y)/Cell),1,100);
            walkable=new bool[width*height]; cost=new float[walkable.Length]; previous=new int[walkable.Length]; closed=new bool[walkable.Length];
            Physics.SyncTransforms();
            for(int z=0;z<height;z++) for(int x=0;x<width;x++)
            {
                Vector3 point=World(z*width+x);
                bool ground=Physics.Raycast(point+Vector3.up,Vector3.down,1.4f,~0,QueryTriggerInteraction.Ignore);
                int count=Physics.OverlapCapsuleNonAlloc(point+Vector3.up*.4f,point+Vector3.up*1.35f,.34f,nearby,~0,QueryTriggerInteraction.Ignore);
                bool blocked=count==nearby.Length;
                for(int n=0;n<count;n++) if(nearby[n] && !(nearby[n] is CharacterController)) blocked=true;
                walkable[z*width+x]=ground && !blocked;
            }
        }
        Vector3 World(int id) => new(min.x+(id%width+.5f)*Cell,.05f,min.y+(id/width+.5f)*Cell);
        int Nearest(Vector3 point)
        {
            int cx=Mathf.Clamp(Mathf.FloorToInt((point.x-min.x)/Cell),0,width-1),cz=Mathf.Clamp(Mathf.FloorToInt((point.z-min.y)/Cell),0,height-1);
            int best=-1; float distance=float.MaxValue;
            for(int radius=0;radius<5;radius++)
            {
                for(int z=Mathf.Max(0,cz-radius);z<=Mathf.Min(height-1,cz+radius);z++)
                    for(int x=Mathf.Max(0,cx-radius);x<=Mathf.Min(width-1,cx+radius);x++)
                    {
                        int id=z*width+x; if(!walkable[id]) continue;
                        float d=(World(id)-point).sqrMagnitude; if(d<distance) { best=id; distance=d; }
                    }
                if(best>=0) return best;
            }
            return -1;
        }
        public List<Vector3> Path(Vector3 from,Vector3 destination)
        {
            int start=Nearest(from),end=Nearest(destination);
            var result=new List<Vector3>(); if(start<0 || end<0) return result;
            System.Array.Fill(cost,float.MaxValue); System.Array.Fill(previous,-1); System.Array.Clear(closed,0,closed.Length);
            open.Clear(); open.Add(start); cost[start]=0;
            int iterations=0;
            while(open.Count>0 && iterations++<walkable.Length)
            {
                int best=0; float score=float.MaxValue;
                for(int i=0;i<open.Count;i++) { int id=open[i]; float f=cost[id]+Vector3.Distance(World(id),World(end)); if(f<score) { best=i; score=f; } }
                int current=open[best]; open.RemoveAt(best); if(current==end) break; closed[current]=true;
                int x=current%width,z=current/width;
                for(int dz=-1;dz<=1;dz++) for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0 && dz==0) continue;
                    int nx=x+dx,nz=z+dz; if(nx<0 || nx>=width || nz<0 || nz>=height) continue;
                    int next=nz*width+nx;
                    if(!walkable[next] || closed[next]) continue;
                    if(dx!=0 && dz!=0 && (!walkable[z*width+nx] || !walkable[nz*width+x])) continue;
                    float candidate=cost[current]+(dx==0 || dz==0 ? 1 : 1.4143f);
                    if(candidate>=cost[next]) continue;
                    cost[next]=candidate; previous[next]=current; if(!open.Contains(next)) open.Add(next);
                }
            }
            if(start!=end && previous[end]<0) return result;
            int cursor=end;
            while(cursor!=start && cursor>=0 && result.Count<walkable.Length) { result.Add(World(cursor)); cursor=previous[cursor]; }
            result.Reverse(); return result;
        }
    }
}
