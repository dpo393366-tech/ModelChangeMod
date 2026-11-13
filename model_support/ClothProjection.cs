using System;
using System.Numerics;

namespace Vow.Performance;

internal static class ClothProjection {
    internal static Vector3 Project(Vector3 world,Vector3 fallback,float[] radii,Vector3[] ca,Vector3[] cb,float scale,Vector3 plane,Vector3 normal,float clearance){
        Vector3 p=world;
        for(int pass=0;pass<2;pass++){
            Vector3 before=p;
            for(int j=0;j<4;j++)p=Capsule(p,ca[j],cb[j],radii[j]*scale,fallback);
            // The full ordered capsule pass has reached a fixed point. Repeating
            // these same constraints produces the same position; floor still runs.
            if(p==before)break;
        }
        float d=Vector3.Dot(p-plane,normal);
        return d<clearance?p+normal*(clearance-d):p;
    }
    static Vector3 Capsule(Vector3 p,Vector3 a,Vector3 b,float radius,Vector3 fallback){
        if(radius<=0)return p;
        Vector3 axis=b-a;float length=Math.Max(axis.LengthSquared(),1E-10f);
        float t=Math.Clamp(Vector3.Dot(p-a,axis)/length,0f,1f);
        Vector3 center=a+axis*t,delta=p-center;float d=delta.LengthSquared();
        if(d>=radius*radius)return p;
        if(d<1E-12f){delta=fallback-axis*(Vector3.Dot(fallback,axis)/length);if(delta.LengthSquared()<1E-10f)delta=Vector3.UnitZ;delta=Vector3.Normalize(delta);}
        else delta/=MathF.Sqrt(d);
        return center+delta*radius;
    }
}
