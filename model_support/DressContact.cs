using System;
using System.Numerics;

namespace Vow.Performance;

// Pure managed broad phase. Candidate contacts still use the original formula,
// in the original order, with all surface vertices and all solver passes intact.
internal sealed class DressContact {
    internal struct Capsule {
        internal Vector3 A,B,Axis,F,Projected;
        internal float Radius2,Axis2,Ad,Aa,Projected2,Radius,Margin;
        internal Capsule(Vector3 a,Vector3 b,float radius,Vector3 forward){
            A=a;B=b;F=Vector3.Normalize(forward);Axis=b-a;Radius2=radius*radius;
            Axis2=Axis.LengthSquared();Ad=Vector3.Dot(Axis,F);Aa=Axis2-Ad*Ad;
            Projected=Axis-F*Ad;Projected2=Math.Max(1E-10f,Projected.LengthSquared());
            Radius=MathF.Sqrt(Radius2);Margin=1E-4f*(1+a.Length()+b.Length());
        }
        internal float Push(Vector3 point){
            Vector3 v=point-A;float d=Vector3.Dot(F,v);Vector3 q=v-F*d;
            float t=Math.Clamp(Vector3.Dot(q,Projected)/Projected2,0f,1f);
            if((q-Projected*t).LengthSquared()>Radius2)return 0;
            float av=Vector3.Dot(Axis,v),u=Axis2*d-av*Ad,w=Axis2*v.LengthSquared()-av*av-Radius2*Axis2;
            float result=0,disc=u*u-Aa*w;
            if(Aa>1E-10f&&disc>=0){float hit=(-u+MathF.Sqrt(disc))/Aa,along=av+hit*Ad;if(along>=0&&along<=Axis2)result=Math.Max(result,hit);}
            disc=Radius2-(v.LengthSquared()-d*d);if(disc>=0)result=Math.Max(result,-d+MathF.Sqrt(disc));
            v=point-B;d=Vector3.Dot(v,F);disc=Radius2-(v.LengthSquared()-d*d);
            if(disc>=0)result=Math.Max(result,-d+MathF.Sqrt(disc));return result;
        }
        internal bool MayHit(Vector3 p,float bound,float margin){
            Vector3 v=p-A,q=v-F*Vector3.Dot(F,v);
            float t=Math.Clamp(Vector3.Dot(q,Projected)/Projected2,0f,1f);
            // A conservative projected sphere encloses EVERY surface offset.
            // Margin covers single-precision projection/translation rounding.
            float radius=Radius+bound+Margin+margin;
            return !((q-Projected*t).LengthSquared()>radius*radius);
        }
    }
    Capsule[] caps=Array.Empty<Capsule>();float[] bounds=Array.Empty<float>();
    Vector3[][] surface;float limit;
    internal long Queries,SkippedCapsules;
    internal bool Prepare(Vector3[][] s,Vector3[] dirs,Vector3[] a,Vector3[] b,float size){
        if(s==null||dirs==null||s.Length!=dirs.Length||a==null||b==null||a.Length<6||b.Length<6||!float.IsFinite(size)||size<=0)return false;
        if(bounds.Length!=s.Length){bounds=new float[s.Length];caps=new Capsule[s.Length*6];}
        for(int i=0;i<s.Length;i++){
            if(s[i]==null||!Finite(dirs[i])||dirs[i].LengthSquared()<1E-10f)return false;
            float bound=0;foreach(var v in s[i]){if(!Finite(v))return false;bound=Math.Max(bound,v.Length());}
            // F is normalized, but allow rounding in I - F F^T as well.
            bounds[i]=bound*1.00001f;
            for(int j=0;j<6;j++){
                if(!Finite(a[j])||!Finite(b[j]))return false;
                caps[i*6+j]=new Capsule(a[j],b[j],(j>=4?.058f:j%2==0?.087f:.065f)*size+.007f*size,dirs[i]);
            }
        }
        surface=s;limit=.5f*size;return true;
    }
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    internal bool TryPush(int i,Vector3 p,out float result){
        result=0;if(surface==null||(uint)i>=(uint)surface.Length||!Finite(p))return false;
        Queries++;int mask=0,offset=i*6;float margin=1E-4f*p.Length();
        for(int j=0;j<6;j++)if(caps[offset+j].MayHit(p,bounds[i],margin))mask|=1<<j;else SkippedCapsules++;
        if(mask==0)return true;
        result=Hit(p,mask,offset);
        foreach(var v in surface[i])result=Math.Max(result,Hit(p+v,mask,offset));
        result=Math.Min(result,limit);return true;
    }
    float Hit(Vector3 p,int mask,int offset){float n=0;for(int j=0;j<6;j++)if((mask&(1<<j))!=0)n=Math.Max(n,caps[offset+j].Push(p));return n;}
}
