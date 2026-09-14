using Godot;
namespace CozyCave;
public partial class Main
{
    private CharacterBody3D cat=null!;
    private Node3D catPose=null!,catBody=null!,catHead=null!,catTail=null!;
    private readonly List<Node3D> catLegs=[];
    private AudioStreamPlayer3D catVoice=null!;
    private Vector3 catDestination=new(-4,0,1);
    private float catTime,catRest=3,catDecision=8,catSound=20,catSave;
    private bool catSleeping;
    private void BuildCat()
    {
        var p=state.CatPosition;
        var at=p is {Length:3} && p.All(float.IsFinite)?new Vector3(p[0],p[1],p[2]):new Vector3(-3,1,2);
        if(Math.Abs(at.X)>7 || Math.Abs(at.Z)>7 || at.Y< -1 || at.Y>5) at=new Vector3(-3,1,2);
        cat=new CharacterBody3D {Name="Cat",Position=at,CollisionLayer=8,CollisionMask=1,FloorSnapLength=.25f};AddChild(cat);cat.SetMeta("item","$cat");
        cat.AddChild(new CollisionShape3D {Position=new Vector3(0,.25f,0),Shape=new CapsuleShape3D {Radius=.2f,Height=.5f}});
        catPose=new Node3D {Scale=Vector3.One*.8f};cat.AddChild(catPose);
        var texture=Vanilla("entity/cat/calico");
        Node3D Part(Vector3 position) {var n=new Node3D {Position=position};catPose.AddChild(n);return n;}
        catBody=Part(new Vector3(0,.5f,-.1f));catBody.RotationDegrees=new Vector3(90,0,0);CatBox(catBody,Vector3.Zero,new Vector3(4,16,6),new Vector2(20,0),texture);
        catHead=Part(new Vector3(0,.65f,.48f));CatBox(catHead,Vector3.Zero,new Vector3(5,4,5),Vector2.Zero,texture);
        CatBox(catHead,new Vector3(0,-.07f,.18f),new Vector3(3,2,2),new Vector2(0,24),texture);
        CatBox(catHead,new Vector3(-.125f,.155f,-.025f),new Vector3(1,1,2),new Vector2(0,10),texture);
        CatBox(catHead,new Vector3(.125f,.155f,-.025f),new Vector3(1,1,2),new Vector2(6,10),texture);
        foreach(var position in new[]{new Vector3(-.075f,.375f,.27f),new Vector3(.075f,.375f,.27f),new Vector3(-.075f,.375f,-.45f),new Vector3(.075f,.375f,-.45f)})
        {var leg=Part(position);CatBox(leg,new Vector3(0,-.1875f,0),new Vector3(2,6,2),new Vector2(0,16),texture);catLegs.Add(leg);}
        catTail=Part(new Vector3(0,.57f,-.62f));catTail.RotationDegrees=new Vector3(45,0,0);
        CatBox(catTail,new Vector3(0,-.25f,0),new Vector3(1,8,1),new Vector2(0,15),texture);
        var tip=new Node3D {Position=new Vector3(0,-.5f,0),RotationDegrees=new Vector3(-35,0,0)};catTail.AddChild(tip);CatBox(tip,new Vector3(0,-.25f,0),new Vector3(1,8,1),new Vector2(4,15),texture);
        catVoice=new AudioStreamPlayer3D {UnitSize=2,MaxDistance=12};cat.AddChild(catVoice);
    }
    private void CatBox(Node parent,Vector3 center,Vector3 pixels,Vector2 offset,Texture2D texture)
    {
        float w=pixels.X,h=pixels.Y,d=pixels.Z;var half=pixels/32;var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
        void Face(Vector3[] ps,Vector3 normal,Rect2 uv)
        {
            var coords=new[]{new Vector2(uv.Position.X,uv.End.Y),uv.End,new Vector2(uv.End.X,uv.Position.Y),uv.Position};
            foreach(int i in new[]{0,1,2,0,2,3}) {st.SetNormal(normal);st.SetUV((coords[i]+offset)/texture.GetSize());st.AddVertex(ps[i]*half);}
        }
        Face([new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1)],Vector3.Back,new(d,d,w,h));
        Face([new(1,-1,-1),new(-1,-1,-1),new(-1,1,-1),new(1,1,-1)],Vector3.Forward,new(2*d+w,d,w,h));
        Face([new(1,-1,1),new(1,-1,-1),new(1,1,-1),new(1,1,1)],Vector3.Right,new(d+w,d,d,h));
        Face([new(-1,-1,-1),new(-1,-1,1),new(-1,1,1),new(-1,1,-1)],Vector3.Left,new(0,d,d,h));
        Face([new(-1,1,1),new(1,1,1),new(1,1,-1),new(-1,1,-1)],Vector3.Up,new(d,0,w,d));
        Face([new(-1,-1,-1),new(1,-1,-1),new(1,-1,-1),new(1,-1,1)],Vector3.Down,new(d+w,0,w,d));
        parent.AddChild(new MeshInstance3D {Mesh=st.Commit(),Position=center,MaterialOverride=new StandardMaterial3D {AlbedoTexture=texture,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
    }
    private void ToggleCatSit()
    {
        state.CatSitting=!state.CatSitting;catSleeping=false;catRest=0;catRoute.Clear();Changed();CatSound("cat_meow");Toast(state.CatSitting?"Cat will stay here. Right-click to let it roam.":"Cat is free to roam.");
    }
    private void CatSound(string sound)
    {
        if(locked || paused || hidden) return;
        catVoice.Stream=GD.Load<AudioStream>("res://Assets/Vanilla/"+sound+".ogg");catVoice.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*.45f));catVoice.Play();
    }
    private void UpdateCat(float dt)
    {
        if(cat==null) return;
        catVoice.StreamPaused=locked||paused||hidden;
        if(locked||paused||hidden) return;
        dt=Math.Min(dt,.1f);catTime+=dt;catDecision-=dt;catSound-=dt;catRest=Math.Max(0,catRest-dt);
        var velocity=cat.Velocity;velocity.Y=cat.IsOnFloor()?-.2f:velocity.Y-15*dt;
        var flat=catDestination-cat.Position;flat.Y=0;
        if(!state.CatSitting && (catDecision<=0 || flat.Length()<.4f))
        {
            catDecision=8+(float)random.NextDouble()*8;catRest=catRoute.Count==0?1+(float)random.NextDouble()*2:0;
            catSleeping=cat.Position.DistanceTo(new Vector3(-5,0,-1))<2.5f;
            if(catSleeping) catRest=18;
            catDestination=random.NextDouble()<.45?new Vector3(-5,0,-1):new Vector3(-4+(float)random.NextDouble()*8,0,-3+(float)random.NextDouble()*7);
        }
        bool moving=!state.CatSitting && catRest<=0;
        if(moving && (catRoute.Count==0 || catRouteGoal.DistanceTo(catDestination)>.1f)) PlanCatRoute();
        if(catRoute.Count>0 && cat.Position.DistanceTo(catRoute.Peek())<.22f) catRoute.Dequeue();
        var direction=catRoute.Count>0?catRoute.Peek()-cat.Position:Vector3.Zero;direction.Y=0;
        moving &= direction.Length()>.08f;
        direction=direction.Normalized();
        if(moving && (cat.Position+direction*.4f).DistanceTo(player.Position)<.7f) moving=false;
        if(moving && cat.IsOnFloor() && catRoute.Peek().Y-cat.Position.Y is > .02f and < .26f) cat.Position=new Vector3(cat.Position.X,catRoute.Peek().Y+.02f,cat.Position.Z);
        var before=cat.Position;
        velocity.X=moving?direction.X*.65f:0;velocity.Z=moving?direction.Z*.65f:0;cat.Velocity=velocity;cat.MoveAndSlide();
        if(moving && cat.Position.DistanceTo(before)<dt*.1f) catStuck+=dt;else catStuck=0;
        if(catStuck>1) {catRoute.Clear();catDecision=0;catRest=0;catStuck=0;}
        if(moving) {cat.Rotation=new Vector3(0,Mathf.LerpAngle(cat.Rotation.Y,Mathf.Atan2(direction.X,direction.Z),dt*5),0);catSleeping=false;}
        bool sitting=state.CatSitting || (!moving && !catSleeping);
        catPose.Rotation=new Vector3(0,0,catSleeping?Mathf.Pi/2:0);catPose.Position=new Vector3(catSleeping?.2f:0,catSleeping?.15f:0,0);
        catBody.RotationDegrees=new Vector3(sitting?35:90,0,0);catBody.Position=new Vector3(0,sitting?.36f:.5f,-.1f);
        catHead.Position=new Vector3(0,sitting?.82f:.65f,sitting?.2f:.48f);
        catHead.Rotation=new Vector3(catSleeping?.15f:0,Mathf.Sin(catTime*.4f)*.08f,0);
        for(int i=0;i<4;i++) catLegs[i].Rotation=new Vector3(moving?Mathf.Sin(catTime*7+(i is 0 or 3?0:Mathf.Pi))*.55f:sitting&&i>=2?-Mathf.Pi/2:0,0,0);
        catTail.Rotation=new Vector3(sitting?1.3f:.7f,Mathf.Sin(catTime*1.2f)*.12f,0);
        if(catSound<=0) {CatSound(catSleeping?"cat_purr":"cat_meow");catSound=25+(float)random.NextDouble()*35;}
        if(cat.Position.Y< -3) cat.Position=new Vector3(-3,1,2);
        catSave+=dt;if(catSave>5) {catSave=0;state.CatPosition=[cat.Position.X,cat.Position.Y,cat.Position.Z];dirty=true;}
    }
}
