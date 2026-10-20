using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

internal static class PatchEngine {
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static TypeDef Type(ModuleDef m,string name){return m.GetTypes().Single(t=>t.FullName==name);}
 static FieldDef Flag(ModuleDef m){return Type(m,"PCL.ModSecret").Fields.Single(f=>f.Name=="RestrictMcDownload");}
 static MethodDef Loader(ModuleDef m){return Type(m,"PCL.PageDownloadInstall").Methods.Single(x=>x.Name=="LoaderInit");}
 static MethodDef Modify(ModuleDef m){return Type(m,"PCL.PageDownloadModify").Methods.Single(x=>x.Name=="LoadInstanceFinished");}
 static int CardIndex(MethodDef method){
  var c=method.Body.Instructions;var found=new List<int>();
  for(int i=0;i<c.Count-2;i++)if(c[i].Operand!=null && c[i].Operand.ToString().Contains("::get_CardMinecraft()") && c[i+1].IsLdcI4() && c[i+2].Operand!=null && c[i+2].Operand.ToString().Contains("::set_Visibility("))found.Add(i+1);
  Need(found.Count==1,"Expected exactly one Minecraft visibility setter");return found[0];
 }
 static bool RealLoader(ModuleDef m){
  var c=Loader(m).Body.Instructions;
  return c.Count(i=>i.OpCode==OpCodes.Ldsfld && i.Operand is IField && ((IField)i.Operand).FullName=="PCL.ModLoader/LoaderTask`2<System.String,PCL.ModDownload/DlClientListResult> PCL.ModDownload::DlClientListLoader")==1
   && !c.Any(i=>i.OpCode==OpCodes.Ldstr) && c.Count(i=>i.OpCode==OpCodes.Call && i.Operand.ToString().Contains("::PageLoaderInit<"))==1;
 }
 internal static object Inspect(string path){
  using(var m=ModuleDefMD.Load(path)){
   var flag=Flag(m);Need(flag.IsLiteral && flag.Constant!=null && flag.Constant.Value is bool,"Expected literal Boolean field");
   return new {assembly=m.Assembly.Name.String,version=m.Assembly.Version.ToString(),restrictMcDownload=(bool)flag.Constant.Value,realVersionLoader=RealLoader(m),minecraftCardVisible=Modify(m).Body.Instructions[CardIndex(Modify(m))].GetLdcI4Value()==0,types=m.GetTypes().Count(),resources=m.Resources.Count};
  }
 }
 static string Body(MethodDef method){return method.HasBody ? string.Join("\n",method.Body.Instructions.Select(i=>i.ToString()).ToArray()) : "";}
 internal static void Verify(string original,string patched){
  using(var a=ModuleDefMD.Load(original))using(var b=ModuleDefMD.Load(patched)){
   Need(a.Assembly.FullName==b.Assembly.FullName,"Assembly identity changed");
   Need(!(bool)Flag(b).Constant.Value && RealLoader(b),"Download flag/loader verification failed");
   var am=Modify(a);var bm=Modify(b);var ac=am.Body.Instructions;var bc=bm.Body.Instructions;
   Need(ac.Count==bc.Count,"Modify method size changed");int k=CardIndex(am);
   Need(bc[CardIndex(bm)].GetLdcI4Value()==0,"Minecraft card is still hidden");
   for(int i=0;i<ac.Count;i++)if(i!=k)Need(ac[i].ToString()==bc[i].ToString(),"Unexpected modify instruction change");
   var methodsA=a.GetTypes().SelectMany(t=>t.Methods).ToArray();var methodsB=b.GetTypes().SelectMany(t=>t.Methods).ToArray();
   string loaderName=Loader(a).FullName;
   Need(methodsA.Length==methodsB.Length,"Method count changed");
   for(int i=0;i<methodsA.Length;i++){
    Need(methodsA[i].FullName==methodsB[i].FullName,"Method identity changed");
    if(methodsA[i].FullName!=loaderName && methodsA[i].FullName!=am.FullName)Need(Body(methodsA[i])==Body(methodsB[i]),"Unexpected change: "+methodsA[i].FullName);
   }
   var fieldsA=a.GetTypes().SelectMany(t=>t.Fields).ToArray();var fieldsB=b.GetTypes().SelectMany(t=>t.Fields).ToArray();
   Need(fieldsA.Length==fieldsB.Length,"Field count changed");
   string flagName=Flag(a).FullName;
   for(int i=0;i<fieldsA.Length;i++){
    Need(fieldsA[i].FullName==fieldsB[i].FullName,"Field identity changed");
    if(fieldsA[i].FullName!=flagName)Need(object.Equals(fieldsA[i].Constant==null?null:fieldsA[i].Constant.Value,fieldsB[i].Constant==null?null:fieldsB[i].Constant.Value),"Other constant changed");
   }
   Need(a.Resources.Count==b.Resources.Count,"Resource count changed");
   for(int i=0;i<a.Resources.Count;i++){
    Need(a.Resources[i].Name==b.Resources[i].Name,"Resource name changed");
    var ra=a.Resources[i] as EmbeddedResource;var rb=b.Resources[i] as EmbeddedResource;
    if(ra!=null)Need(rb!=null && ra.CreateReader().ToArray().SequenceEqual(rb.CreateReader().ToArray()),"Embedded resource changed");
   }
  }
 }
 internal static void Patch(string original,string output){
  Need(!File.Exists(output),"Output already exists");
  using(var m=ModuleDefMD.Load(original)){
   Need(m.Assembly.Name=="Plain Craft Launcher 2" && m.Assembly.Version.ToString()=="2.99.0.0","Unsupported assembly identity/version");
   Need(Flag(m).IsLiteral && (bool)Flag(m).Constant.Value,"Expected restriction=True");
   var method=Loader(m);var c=method.Body.Instructions;
   Need(c.Count==30,"Unexpected LoaderInit structure");
   int start=c.ToList().FindIndex(i=>i.OpCode==OpCodes.Ldstr && i.Operand.ToString()=="关闭下载");
   int end=c.ToList().FindIndex(i=>i.OpCode==OpCodes.Newobj && i.Operand.ToString().StartsWith("System.Void PCL.ModLoader/LoaderTask`2"));
   Need(start==10 && end==22,"Unexpected dummy-loader block: "+start+","+end);
   var dl=Type(m,"PCL.ModDownload").Fields.Single(f=>f.Name=="DlClientListLoader");
   for(int i=end;i>=start;i--)c.RemoveAt(i);
   c.Insert(start,Instruction.Create(OpCodes.Ldsfld,dl));method.Body.OptimizeBranches();
   var modify=Modify(m);int k=CardIndex(modify);Need(modify.Body.Instructions[k].GetLdcI4Value()==2,"Expected collapsed Minecraft card");
   modify.Body.Instructions[k].OpCode=OpCodes.Ldc_I4_0;modify.Body.Instructions[k].Operand=null;
   Flag(m).Constant=new ConstantUser(false);
   var options=new ModuleWriterOptions(m);options.MetadataOptions.Flags|=MetadataFlags.PreserveAll;
   m.Write(output,options);
  }
  Verify(original,output);
 }
}
