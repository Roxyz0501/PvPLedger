using Dalamud.Bindings.ImGui;
unsafe static class Renderer {
 public static void Render(ImDrawDataPtr dd, Dictionary<ulong,(nint,int,int)> textures,string output)
 {
  const int w=1440,h=900; byte[] pixels=new byte[w*h*4];
  for(int i=0;i<pixels.Length;i+=4) { pixels[i]=37; pixels[i+1]=28; pixels[i+2]=20; pixels[i+3]=255; }
  for(int n=0;n<dd.CmdListsCount;n++)
  {
   var list=new ImDrawListPtr(dd.CmdLists[n]);
   for(int c=0;c<list.CmdBuffer.Size;c++)
   {
    var cmd=list.CmdBuffer[c];
    var texture=textures[cmd.TextureId.Handle]; var atlas=(byte*)texture.Item1; var aw=texture.Item2; var ah=texture.Item3;
    for(int i=0;i<cmd.ElemCount;i+=3)
    {
     var a=list.VtxBuffer[(int)cmd.VtxOffset+list.IdxBuffer[(int)cmd.IdxOffset+i]];
     var b=list.VtxBuffer[(int)cmd.VtxOffset+list.IdxBuffer[(int)cmd.IdxOffset+i+1]];
     var d=list.VtxBuffer[(int)cmd.VtxOffset+list.IdxBuffer[(int)cmd.IdxOffset+i+2]];
     var xmin=Math.Max(0,(int)Math.Max(cmd.ClipRect.X,MathF.Min(a.Pos.X,MathF.Min(b.Pos.X,d.Pos.X))));
     var xmax=Math.Min(w-1,(int)Math.Min(cmd.ClipRect.Z-1,MathF.Max(a.Pos.X,MathF.Max(b.Pos.X,d.Pos.X))));
     var ymin=Math.Max(0,(int)Math.Max(cmd.ClipRect.Y,MathF.Min(a.Pos.Y,MathF.Min(b.Pos.Y,d.Pos.Y))));
     var ymax=Math.Min(h-1,(int)Math.Min(cmd.ClipRect.W-1,MathF.Max(a.Pos.Y,MathF.Max(b.Pos.Y,d.Pos.Y))));
     var det=(b.Pos.Y-d.Pos.Y)*(a.Pos.X-d.Pos.X)+(d.Pos.X-b.Pos.X)*(a.Pos.Y-d.Pos.Y);
     if(Math.Abs(det)<0.001) continue;
     for(int y=ymin;y<=ymax;y++) for(int x=xmin;x<=xmax;x++)
     {
      var u=((b.Pos.Y-d.Pos.Y)*(x+0.5f-d.Pos.X)+(d.Pos.X-b.Pos.X)*(y+0.5f-d.Pos.Y))/det;
      var v=((d.Pos.Y-a.Pos.Y)*(x+0.5f-d.Pos.X)+(a.Pos.X-d.Pos.X)*(y+0.5f-d.Pos.Y))/det;
      var z=1-u-v; if(u<0||v<0||z<0)continue;
      var uv=a.Uv*u+b.Uv*v+d.Uv*z;
      var tex=(Math.Clamp((int)(uv.Y*ah),0,ah-1)*aw+Math.Clamp((int)(uv.X*aw),0,aw-1))*4;
      var alpha=(((a.Col>>24)&255)*u+((b.Col>>24)&255)*v+((d.Col>>24)&255)*z)/255f*atlas[tex+3]/255f;
      var index=(y*w+x)*4;
      for(int k=0;k<3;k++)
      {
       int shift=k*8; var value=(((a.Col>>shift)&255)*u+((b.Col>>shift)&255)*v+((d.Col>>shift)&255)*z)*atlas[tex+k]/255f;
       pixels[index+2-k]=(byte)Math.Clamp(value*alpha+pixels[index+2-k]*(1-alpha),0,255);
      }
     }
    }
   }
  }
  using var writer=new BinaryWriter(File.Create(output)); writer.Write((ushort)0x4d42); writer.Write(54+pixels.Length);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(w);writer.Write(-h);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(0);writer.Write(pixels.Length);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(pixels);
 }

}
