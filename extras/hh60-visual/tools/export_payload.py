"""Export decoded, validated mesh/texture data; never write a Unity AssetBundle."""
from __future__ import annotations
import argparse,json,hashlib,re,functools,collections
from pathlib import Path
from bundle_reader import Bundle, Slice, UnityPy, verify_donor, safe_output, DONOR_VERSION, DONOR_SHA256
from UnityPy.helpers.MeshHelper import MeshHandler
import numpy as np
# Fixed GameObject IDs for the SHA-pinned 1.1.3 donor; do not reuse for another release.
ACTORS={'Pilot00':32,'Pilot01':12,'Actor_GunnerLeft':21,'Actor_GunnerRight':18,'Actor_Crew00':19}

def sha(b):return hashlib.sha256(b).hexdigest().upper()
def xyz(v):return [float(v.x),float(v.y),float(v.z)]
def quat(v):return [float(v.x),float(v.y),float(v.z),float(v.w)]
def trs(pos,rot,scale):
 x,y,z,w=rot; q=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
 [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
 [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
 m=np.eye(4);m[:3,:3]=q@np.diag(scale);m[:3,3]=pos;return m
def ident(o):return o.assets_file.name+':'+str(o.path_id)
def matrix4(m):return np.array([[getattr(m,f'e{r}{c}') for c in range(4)] for r in range(4)],dtype=np.float64)

def main():
 ap=argparse.ArgumentParser(description=__doc__)
 ap.add_argument('--bundle',required=True,type=Path,help='Local supported 1.1.3 icebreaker_scenes.bundle')
 ap.add_argument('--poses',required=True,type=Path,help='Local crew_pose.py JSON generated from the same donor')
 ap.add_argument('--output',required=True,type=Path,help='New or empty local payload directory; never commit generated assets')
 args=ap.parse_args();TARGET=verify_donor(args.bundle);OUT=safe_output(TARGET,args.output,directory=True)
 POSE=args.poses.expanduser().resolve(strict=True)
 if POSE.samefile(TARGET):raise ValueError('--poses must not be the donor bundle')
 if OUT==POSE or OUT in POSE.parents:raise ValueError('--output must not contain or overwrite --poses')
 pose=json.loads(POSE.read_text(encoding='utf8'))
 if pose.get('donor_sha256')!=DONOR_SHA256:raise ValueError('Pose donor SHA256 does not match the supported bundle')
 b=Bundle(TARGET);e=UnityPy.Environment(path=str(TARGET.parent))
 for n in b.nodes:
  if 'cutscene' in n['path'] or n['path'].endswith('.sharedAssets'):e.load_file(Slice(b,n),name=n['path'])
 s=e.files['BuildPlayer-Icebreaker_cutscene_01']
 def transform(go):return next(c.component.deref() for c in go.read().m_Component if c.component.type.name=='Transform')
 roots=[('aircraft',transform(s.objects[948]))]+[(name,transform(s.objects[pid])) for name,pid in ACTORS.items()]
 overrides={}
 for actor in pose['actors']:
  if actor['actor'] in ACTORS:overrides.update(actor['transform_overrides'])
 # Aircraft animation overrides are kept separately by the pose exporter.
 overrides.update(pose.get('overrides',{}))
 def local(o):
  t=o.read();d=overrides.get(str(o.path_id),{})
  return dict(position=d.get('position',xyz(t.m_LocalPosition)),rotation=d.get('rotation',quat(t.m_LocalRotation)),scale=d.get('scale',xyz(t.m_LocalScale)))
 helicopter=transform(s.objects[17]) # common Helicopter_00 coordinate space, no cinematic flight path.
 @functools.lru_cache(None)
 def world(pid):
  o=s.objects[pid];t=o.read();d=local(o);m=trs(d['position'],d['rotation'],d['scale'])
  if t.m_Father and t.m_Father.path_id!=helicopter.path_id:m=world(t.m_Father.path_id)@m
  return m
 selected={};owner={};excluded=[]
 for label,root in roots:
  stack=[root]
  while stack:
   o=stack.pop();t=o.read();go=t.m_GameObject.read();name=go.m_Name
   if label=='aircraft' and any(x in name.lower() for x in ('rope','muzzle','vfx','particle')):
    excluded.append(name);continue
   selected[o.path_id]=o;owner[o.path_id]=label
   stack.extend(c.deref() for c in t.m_Children)
 # Native LOD groups specify which renderers actually form LOD0; do not draw all weapon LODs at once.
 excluded_renderers=set()
 for tr in selected.values():
  for cp in tr.read().m_GameObject.read().m_Component:
   if cp.component.type.name!='LODGroup':continue
   group=cp.component.read()
   lods=group.m_LODs or []
   def refs(lod):return {x.renderer.path_id for x in lod.renderers if x.renderer}
   if lods:
    kept=refs(lods[0]);excluded_renderers.update(set().union(*(refs(l) for l in lods[1:]))-kept)
 nodes=[dict(id='root',name='TSC_HH60_VISUAL',parent=None,position=[0,0,0],rotation=[0,1,0,0],scale=[1,1,1])]
 # Preserve aircraft rotor hierarchy; bake crew vertices into a shared, static pose.
 for pid,o in selected.items():
  if owner[pid]!='aircraft':continue
  t=o.read();d=local(o);parent=str(t.m_Father.path_id) if t.m_Father.path_id in selected else 'root'
  nodes.append(dict(id=str(pid),name=t.m_GameObject.read().m_Name,parent=parent,**d))
 for name in ACTORS:nodes.append(dict(id='crew-'+name,name=name,parent='root',position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1]))
 meshes=[];materials={};textures={};renderers=[];files=[];warnings=[];bounds=collections.defaultdict(list)
 for sub in ('meshes','textures'):(OUT/sub).mkdir(parents=True,exist_ok=True)
 def write(file,data):
  (OUT/file).write_bytes(data);files.append(dict(file=file,sha256=sha(data)))
 def texture(o):
  key=ident(o)
  if key in textures:return key
  d=o.read();raw=bytes(d.image_data)
  assert raw and (not d.m_StreamData or not d.m_StreamData.path),'only explicit inlined texture bytes accepted'
  dimension='Cube' if o.type.name=='Cubemap' else '2D'; assert o.type.name in ('Texture2D','Cubemap')
  file=f'textures/{len(textures):04d}.bin';fmt=int(d.m_TextureFormat)
  # All expected source textures use Unity's well-defined block or byte layouts.
  def mip_size(w,h):
   if fmt in (10,28):return max(1,(w+3)//4)*max(1,(h+3)//4)*8
   if fmt in (12,24,25,26,27):return max(1,(w+3)//4)*max(1,(h+3)//4)*16
   if fmt in (1,3,4,5,7,9,14):return w*h*{1:1,3:3,4:4,5:4,7:2,9:2,14:4}[fmt]
   raise ValueError('Unvalidated texture format '+str(fmt))
  sizes=[mip_size(max(1,d.m_Width>>i),max(1,d.m_Height>>i)) for i in range(d.m_MipCount)]
  assert len(raw)==sum(sizes)*(6 if dimension=='Cube' else 1),(d.m_Name,fmt,len(raw),sizes)
  textures[key]=dict(id=key,name=d.m_Name,file=file,width=d.m_Width,height=d.m_Height,format=fmt,
   mipCount=d.m_MipCount,linear=(d.m_ColorSpace==0),dimension=dimension,faceMipSizes=sizes)
  write(file,raw);return key
 def material(o):
  key=ident(o)
  if key in materials:return key
  d=o.read();shader=d.m_Shader.read();donor_shader=shader.m_ParsedForm.m_Name
  shadername={'Custom/Icebreaker Decal':'p0/Reflective/Bumped Specular SMap','Custom/OpticGlass':'EFT/Glass'}.get(donor_shader,donor_shader)
  item=dict(id=key,name=d.m_Name,shader=shadername,donorShader=donor_shader,textures=[],floats=[],colors=[],keywords=[],renderQueue=d.m_CustomRenderQueue)
  if shadername!=donor_shader:warnings.append('Native shader alias: '+d.m_Name+' '+donor_shader+' -> '+shadername)
  materials[key]=item
  for prop,tex in d.m_SavedProperties.m_TexEnvs:
   if not tex.m_Texture:continue
   to=tex.m_Texture.deref()
   if to.type.name not in ('Texture2D','Cubemap'):
    warnings.append('Skipped non-texture '+prop+' '+to.type.name);continue
   item['textures'].append(dict(property=prop,texture=texture(to),scale=[tex.m_Scale.x,tex.m_Scale.y],offset=[tex.m_Offset.x,tex.m_Offset.y]))
  item['floats']=[dict(name=k,value=v) for k,v in d.m_SavedProperties.m_Floats]
  item['colors']=[dict(name=k,value=[v.r,v.g,v.b,v.a]) for k,v in d.m_SavedProperties.m_Colors]
  kw=getattr(d,'m_ShaderKeywords','') or ''
  item['keywords']=kw.split() if isinstance(kw,str) else list(kw)
  item['keywords']+=list(getattr(d,'m_ValidKeywords',[]) or [])
  item['keywords']=sorted(set(item['keywords']))
  return key
 for pid,tr in selected.items():
  t=tr.read();go=t.m_GameObject.read();label=owner[pid]
  for cp in go.m_Component:
   obj=cp.component.deref()
   if obj.type.name not in ('MeshRenderer','SkinnedMeshRenderer') or obj.path_id in excluded_renderers:continue
   d=obj.read()
   if not d.m_Enabled:continue
   if d.m_Materials and all(p and p.read().m_Shader.read().m_ParsedForm.m_Name=='CW FX/Collimator' for p in d.m_Materials):
    excluded.append(go.m_Name+' (unneeded functional gun optic reticle)');continue
   if re.search(r'[_ ]LOD[1-9](?:$|_)',go.m_Name,re.I):continue
   if int(d.m_CastShadows)==3:continue # ShadowsOnly duplicates are not visible model parts.
   if obj.type.name=='MeshRenderer':
    mf=next((c.component.read() for c in go.m_Component if c.component.type.name=='MeshFilter'),None)
    if not mf or not mf.m_Mesh:continue
    mo=mf.m_Mesh.deref()
   else:
    if not d.m_Mesh:continue
    mo=d.m_Mesh.deref()
   md=mo.read();h=MeshHandler(md);h.process();v=np.asarray(h.m_Vertices,dtype=np.float64)[:,:3]
   normals=np.asarray(h.m_Normals,dtype=np.float64)[:,:3] if h.m_Normals else np.zeros_like(v)
   uv=np.asarray(h.m_UV0,dtype=np.float32)[:,:2] if h.m_UV0 else np.zeros((len(v),2),np.float32)
   wm=world(pid);v4=np.c_[v,np.ones(len(v))];nw=normals
   if obj.type.name=='SkinnedMeshRenderer':
    assert h.m_BoneWeights and h.m_BoneIndices and d.m_Bones,(go.m_Name,'missing skinning data')
    weights=np.asarray(h.m_BoneWeights);bi=np.asarray(h.m_BoneIndices,dtype=np.int32)
    assert weights.shape==bi.shape and weights.shape[0]==len(v)
    assert np.allclose(weights.sum(1),1,atol=.01),(go.m_Name,'bone weight sum')
    matrices=[]
    for bone,bp in zip(d.m_Bones,md.m_BindPose):matrices.append(world(bone.path_id)@matrix4(bp))
    matrices=np.asarray(matrices);posed=np.zeros_like(v);posedn=np.zeros_like(normals)
    for j in range(weights.shape[1]):
     assert np.all((bi[:,j]>=0)&(bi[:,j]<len(matrices)))
     m=matrices[bi[:,j]];posed+=np.einsum('nij,nj->ni',m[:,:3,:],v4)*weights[:,j,None]
     posedn+=np.einsum('nij,nj->ni',m[:,:3,:3],normals)*weights[:,j,None]
    if label=='aircraft':
     inv=np.linalg.inv(wm);v=(np.c_[posed,np.ones(len(posed))]@inv.T)[:,:3];nw=posedn@inv[:3,:3].T
    else:v=posed;nw=posedn
   elif label!='aircraft':
    v=(v4@wm.T)[:,:3];nw=normals@np.linalg.inv(wm[:3,:3])
   nw/=np.maximum(np.linalg.norm(nw,axis=1)[:,None],1e-12)
   tris=[np.asarray(x,dtype=np.uint32).reshape(-1) for x in h.get_triangles()]
   idx=np.concatenate(tris);assert idx.size%3==0 and idx.max()<len(v)
   assert np.isfinite(v).all() and np.isfinite(nw).all() and np.isfinite(uv).all()
   assert len(uv)==len(v)
   mid=f'mesh-{len(meshes):04d}';file=f'meshes/{len(meshes):04d}.bin';offset=0;subs=[]
   for tri in tris:subs.append(dict(offset=offset,count=len(tri)));offset+=len(tri)
   blob=v.astype('<f4').tobytes()+nw.astype('<f4').tobytes()+uv.astype('<f4').tobytes()+idx.astype('<u4').tobytes()
   write(file,blob);meshes.append(dict(id=mid,name=md.m_Name,file=file,vertexCount=len(v),indexCount=len(idx),submeshes=subs,owner=label))
   node=str(pid)
   if label!='aircraft':
    node='crew-render-'+str(len(meshes))
    nodes.append(dict(id=node,name=go.m_Name,parent='crew-'+label,position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1]))
   renderers.append(dict(node=node,mesh=mid,materials=[material(p.deref()) for p in d.m_Materials],enabled=True,owner=label))
   assert len(renderers[-1]['materials'])==len(subs),(go.m_Name,'material/submesh mismatch')
   # Bounds in the common aircraft coordinate system, before final TSC placement.
   bounds[label].append((np.c_[v,np.ones(len(v))]@wm.T)[:,:3] if label=='aircraft' else v)
 assert set(ACTORS)<=set(bounds),'all five crew must have actual geometry'
 box={k:dict(min=np.concatenate(v).min(0).tolist(),max=np.concatenate(v).max(0).tolist()) for k,v in bounds.items()}
 # Default export must already be an upright aircraft; do not silently rotate individual crew.
 sizes=np.array(box['aircraft']['max'])-np.array(box['aircraft']['min'])
 assert sizes[1]<10 and sizes[2]>10,('aircraft orientation not upright',sizes)
 nodes[0]['position'][1]=-0.462454273638-box['aircraft']['min'][1]
 for name in ACTORS:
  lo=np.array(box[name]['min']);hi=np.array(box[name]['max'])
  assert np.max(np.abs(np.r_[lo,hi]))<20,(name,'pose outside aircraft envelope',lo,hi)
 scene=dict(schema=2,rootName='TSC_HH60_VISUAL',nodes=nodes,meshes=meshes,textures=list(textures.values()),materials=list(materials.values()),
  renderers=renderers,crew=[dict(name=n,sourceActor=pid,pose='baked source generic animation',meshCount=sum(m['owner']==n for m in meshes)) for n,pid in ACTORS.items()],
  files=files,source=DONOR_VERSION+'/icebreaker_scenes.bundle',bounds=box,warnings=warnings,excluded=excluded,
  notIncluded=['rope geometry/animation','AI','new extraction behavior','Unity AssetBundle loading'])
 (OUT/'scene.json').write_text(json.dumps(scene,ensure_ascii=False,indent=2),encoding='utf8')
 summary=dict(meshes=len(meshes),vertices=sum(x['vertexCount'] for x in meshes),triangles=sum(x['indexCount']//3 for x in meshes),
  textures=len(textures),materials=len(materials),renderers=len(renderers),crew=scene['crew'],bounds=box,bytes=sum((OUT/x['file']).stat().st_size for x in files),shaders=sorted(set(m['shader'] for m in materials.values())),warnings=warnings)
 (OUT/'export-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8');b.f.close();print(json.dumps(summary,ensure_ascii=False,indent=2))

if __name__=='__main__':main()
