"""Read-only extraction of five HH60 crew static poses from native generic clips.
Exports local TRS overrides keyed by the original source Transform path_id.
No Unity bundle is written, no scripts/AI/Animator are run.
"""
from __future__ import annotations
import json, math, struct, zlib, bisect, argparse
from pathlib import Path
from collections import defaultdict
from bundle_reader import Bundle, Slice, UnityPy, verify_donor, safe_output, DONOR_VERSION, DONOR_SHA256
import numpy as np
# Fixed source Transform and AnimationClip IDs for the SHA-pinned 1.1.3 donor.
ACTORS=[('Pilot00',4947,1190),('Pilot01',4929,1193),('Actor_GunnerLeft',4938,1164),('Actor_GunnerRight',4935,1175),('Actor_Crew00',4936,1172)]

def vec(v):return [v.x,v.y,v.z]+([v.w] if hasattr(v,'w') else [])
def local(t):return {'position':vec(t.m_LocalPosition),'rotation':vec(t.m_LocalRotation),'scale':vec(t.m_LocalScale)}
def matrix(d):
 x,y,z,w=d['rotation'];q=np.array([x,y,z,w],dtype=float);q/=np.linalg.norm(q);x,y,z,w=q
 m=np.eye(4);m[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])@np.diag(d['scale']);m[:3,3]=d['position'];return m

def read_clip(d):
 data=d['m_MuscleClip']['m_Clip']['data'];st=data['m_StreamedClip'];u=st['data'];raw=struct.pack('<%dI'%len(u),*u);pos=0;curves=defaultdict(list)
 while pos<len(raw):
  t,n=struct.unpack_from('<fI',raw,pos);pos+=8
  for _ in range(n):
   index,*co=struct.unpack_from('<I4f',raw,pos);pos+=20
   if math.isfinite(t):curves[index].append((t,co))
 assert pos==len(raw)
 return {'curves':curves,'stream_count':st['curveCount'],'dense':data['m_DenseClip'],'constant':data['m_ConstantClip']['data']}

def sample(c,t):
 values=[];count=c['stream_count'];missing=[]
 for i in range(count):
  keys=c['curves'].get(i,[])
  if not keys:missing.append(i);values.append(0);continue
  times=[k[0] for k in keys];ix=max(0,bisect.bisect_right(times,t+1e-7)-1);kt,co=keys[ix];dt=max(0,t-kt)
  values.append(((co[0]*dt+co[1])*dt+co[2])*dt+co[3])
 assert not missing,missing
 dc=c['dense'];nc=dc['m_CurveCount']
 if nc:
  frame=max(0,min(dc['m_FrameCount']-1,(t-dc['m_BeginTime'])*dc['m_SampleRate']));fi=int(frame);fj=min(fi+1,dc['m_FrameCount']-1);alpha=frame-fi;arr=dc['m_SampleArray']
  values.extend(arr[fi*nc+i]*(1-alpha)+arr[fj*nc+i]*alpha for i in range(nc))
 values.extend(c['constant']);return values

def euler_unity(v):
 x,y,z=[math.radians(a)*.5 for a in v]
 def mul(a,b):
  x,y,z,w=a;X,Y,Z,W=b
  return [w*X+x*W+y*Z-z*Y,w*Y-x*Z+y*W+z*X,w*Z+x*Y-y*X+z*W,w*W-x*X-y*Y-z*Z]
 return mul(mul([0,0,math.sin(z),math.cos(z)],[0,math.sin(y),0,math.cos(y)]),[math.sin(x),0,0,math.cos(x)])

def export_pose(scene,asset,name,tid,clipid,time):
 nodes={};parents={};paths={};stack=[(tid,'')]
 while stack:
  k,p=stack.pop();t=scene.objects[k].read();nodes[k]=local(t);parents[k]=t.m_Father.path_id if t.m_Father else None;paths[k]=p
  for child in t.m_Children:
   ct=child.read();cn=ct.m_GameObject.read().m_Name;stack.append((child.path_id,p+'/'+cn if p else cn))
 # The cutscene Actor wrapper contains one main Animator; nested weapon animators must not change hash root.
 main=[]
 for k,p in paths.items():
  if p.count('/')==0:
   go=scene.objects[k].read().m_GameObject.read()
   if any(c.component.type.name=='Animator' for c in go.m_Component):main.append((k,p))
 assert len(main)==1,(name,main)
 animid,animpath=main[0];hashes={0:animid};relative={animid:''}
 for k,p in paths.items():
  if (not animpath and p) or p.startswith(animpath+'/'):
   rel=p[len(animpath)+1:] if animpath else p;h=zlib.crc32(rel.encode());assert h not in hashes,(name,h);hashes[h]=k;relative[k]=rel
 d=asset.objects[clipid].read_typetree();c=read_clip(d);values=sample(c,time);start=0;overrides={};unresolved=[];bindings=[]
 for b in d['m_ClipBindingConstant']['genericBindings']:
  attr=b['attribute'];is_tr=b['typeID']==4;num=(4 if attr==2 else 3) if is_tr and attr in [1,2,3,4] else 1;v=values[start:start+num];start+=num
  row={'hash':b['path'],'attribute':attr,'typeID':b['typeID'],'value':v}
  k=hashes.get(b['path']);row['transform_id']=k
  if k is None:unresolved.append(row);continue
  row['path']=relative[k];bindings.append(row)
  if is_tr and attr in [1,2,3,4]:
   field={1:'position',2:'rotation',3:'scale',4:'rotation'}[attr]
   assert len(v)==num and all(math.isfinite(x) for x in v),(name,b,v)
   if attr==4:v=euler_unity(v)
   if attr==2:
    mag=math.sqrt(sum(x*x for x in v));assert .99<mag<1.01,(name,b,v,mag)
    v=[x/mag for x in v]
   overrides.setdefault(str(k),{})[field]=v
  else:unresolved.append(row)
 assert start==len(values),(name,start,len(values))
 assert all((r['transform_id'] is None and r['hash'] in [1154205328,1950820344,2368997458,1390682545]) or (r['typeID']==95 and r['attribute'] in range(7,14)) for r in unresolved),(name,unresolved)
 posed={k:dict(v,**overrides.get(str(k),{})) for k,v in nodes.items()};cache={}
 def world(k):
  if k in cache:return cache[k]
  m=matrix(posed[k]);p=parents[k]
  if p in nodes:m=world(p)@m
  cache[k]=m;return m
 joints={}
 for k,path in relative.items():
  n=path.rsplit('/',1)[-1]
  if n.startswith('Base Human') or n=='Root_Joint':joints[path]={'transform_id':k,'parent_id':parents[k],'position':world(k)[:3,3].tolist()}
 changed=sum(any(np.max(np.abs(np.array(nodes[int(k)][field])-v))>1e-4 for field,v in fields.items()) for k,fields in overrides.items())
 return {'actor':name,'actor_transform_id':tid,'animator_transform_id':animid,'animator_relative_path':animpath,'clip_id':clipid,'clip_name':d['m_Name'],'sample_time':time,'clip_seconds':d['m_MuscleClip']['m_StopTime']-d['m_MuscleClip']['m_StartTime'],'bindings':len(bindings),'sampled_values':len(values),'overridden_transforms':len(overrides),'changed_from_rest':changed,'unresolved_bindings':unresolved,'transform_overrides':overrides,'joints_actor_frame':joints,'binding_details':bindings}

def main():
 ap=argparse.ArgumentParser(description=__doc__)
 ap.add_argument('--bundle',required=True,type=Path,help='Local icebreaker_scenes.bundle from the supported 1.1.3 release')
 ap.add_argument('--output',required=True,type=Path,help='New local crew-poses.json file (never commit generated assets)')
 ap.add_argument('--time',type=float,default=0.0,help='Pose sample time; validated default is 0 seconds')
 args=ap.parse_args()
 if not math.isfinite(args.time) or args.time<0:ap.error('--time must be finite and nonnegative')
 TARGET=verify_donor(args.bundle);OUT=safe_output(TARGET,args.output,directory=False)
 OUT.parent.mkdir(parents=True,exist_ok=True)
 b=Bundle(TARGET);e=UnityPy.Environment(path=str(TARGET.parent))
 for n in b.nodes:
  if 'cutscene' in n['path'] or n['path'].endswith('.sharedAssets'):e.load_file(Slice(b,n),name=n['path'])
 scene=e.files['BuildPlayer-Icebreaker_cutscene_01'];asset=e.files['BuildPlayer-Icebreaker_cutscene_01.sharedAssets'];poses=[]
 for name,tid,clip in ACTORS:
  pose=export_pose(scene,asset,name,tid,clip,args.time);poses.append(pose)
  print('POSE',name,'clip',clip,'overrides',pose['overridden_transforms'],'changed',pose['changed_from_rest'],'unresolved',len(pose['unresolved_bindings']),flush=True)
  aircraft=[]
 for clipid in [1183,1186,1160]:
  pose=export_pose(scene,asset,'HH60',5988,clipid,args.time);aircraft.append(pose)
  print('AIRCRAFT',clipid,'overrides',pose['overridden_transforms'],'unresolved',len(pose['unresolved_bindings']))
 overrides={}
 # Right clip carries constant left-gun channels: apply the matching left clip LAST.
 for pose in poses+[aircraft[0],aircraft[2],aircraft[1]]:
  for k,v in pose['transform_overrides'].items():overrides.setdefault(k,{}).update(v)
 report={'overrides':overrides,'aircraft_clips':aircraft,'euler_convention':'Imported raw clip Euler sampled using XYZ axes (Rz Ry Rx), not Transform inspector ZXY. Offline anatomical/seat pose check; not an in-game animation validation.','ignored_binding_explanation':'Every actor has 12 missing helper-node channels (Bend_Goal_Right/Left, IK_S_RPalm/LPalm) and 7 Animator root-motion channels. No deform bone binding is missing.','source_bundle':DONOR_VERSION+'/icebreaker_scenes.bundle','donor_sha256':DONOR_SHA256,'source_scene':scene.name,'scope':'Static local Transform poses sampled from original Generic animation curves. No ropes, behaviours, AI or source modification. Actor coordinate frame is Helicopter_00/Crew (identity TRS); geometry adapter must preserve source aircraft-to-crew relation.','sample_time':args.time,'actors':poses}
 p=OUT;p.write_text(json.dumps(report,indent=2),encoding='utf8');reopen=json.loads(p.read_text(encoding='utf8'));assert len(reopen['actors'])==5
 b.f.close();print('REPORT',p,'bytes',p.stat().st_size,flush=True)

if __name__=='__main__':main()
