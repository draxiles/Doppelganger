"""Fetch only selected art from Unity's public FPS Sample; never import its game code.
The staged files retain the upstream directory layout, metadata and license.
"""
import concurrent.futures
import hashlib
import json
from pathlib import Path
from urllib.parse import quote
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / 'ThirdPartyStaging' / 'UnityFpsSample'
TREE = json.loads((ROOT / 'Temp/DoppelgangerChecks/fpssample-tree.json').read_text(encoding='utf-8-sig'))['tree']
RAW = 'https://raw.githubusercontent.com/Unity-Technologies/FPSSample/master/'
MEDIA = 'https://media.githubusercontent.com/media/Unity-Technologies/FPSSample/master/'

def wanted(path):
    if path in ['LICENSE.md', 'Assets/LICENSE.md']:
        return True
    if path in ['Assets/Prefabs/Characters/Terraformer/Terraformer_A_1P.prefab',
                'Assets/Prefabs/Characters/Robot/Robot_A_1P.prefab',
                'Assets/Prefabs/Weapons/TerraformerWeapon/Item_TerraformerWeapon_1P.prefab',
                'Assets/Prefabs/Weapons/RobotWeapon/Item_RobotWeapon_1P.prefab']:
        return True
    if path in ['Assets/Models/Characters/Terraformer/Terraformer_A--FirstPerson.fbx',
                'Assets/Models/Characters/Robot/Robot_A--FirstPerson.fbx',
                'Assets/Models/Weapons/Terraformer_Weapon_A/Terraformer_Weapon_A_1P.fbx',
                'Assets/Models/Weapons/Robot_Weapon_A/Robot_Weapon_A.fbx']:
        return True
    if path.startswith(('Assets/Animation/Characters/Terraformer_1P/Clips/', 'Assets/Animation/Characters/Robot_1P/Clips/')):
        return path.rsplit('/',1)[1] in ['Firstperson--Idle.anim.fbx','Firstperson--Idle_Weapon.anim.fbx',
            'Firstperson--Run.anim.fbx','Firstperson--Run_Weapon.anim.fbx','Firstperson--Sprint.anim.fbx',
            'Firstperson--Sprint_Weapon.anim.fbx','Firstperson--Shoot.anim.fbx','Firstperson--Shoot_Weapon.anim.fbx',
            'Firstperson--Reload.anim.fbx','Firstperson--Reload_Weapon.anim.fbx']
    if path.startswith(('Assets/Textures/Characters/Terraformer_A/','Assets/Textures/Characters/Robot_A/')):
        return 'Firstperson' in path and any(word in path for word in ['Albedo','Diffuse','Normal','Mask'])
    if path.startswith('Assets/Textures/Weapons/'):
        return any(folder in path for folder in ['/Terraformer_Weapon_A/','/Robot_Weapon_A/']) and not any(word in path.lower() for word in ['height','bnm','transmission'])
    if path.startswith(('Assets/Materials/Characters/Terraformer_A_1P/','Assets/Materials/Characters/Robot_A_1P/',
                        'Assets/Materials/Weapons/Terraformer_Weapon_A_1P/','Assets/Materials/Weapons/Robot_Weapon_A/')):
        return path.endswith('.mat') and 'Projectile' not in path
    return False

all_files={item['path']:item for item in TREE if item['type']=='blob'}
selected={path for path in all_files if wanted(path)}
selected.update(path+'.meta' for path in list(selected) if path+'.meta' in all_files)

def read(url):
    with urlopen(Request(url,headers={'User-Agent':'Doppelganger-art-import'}),timeout=60) as response:
        return response.read()

def fetch(path):
    target=STAGE/path
    if target.is_file() and target.stat().st_size>0:
        return {'path':path,'bytes':target.stat().st_size,'cached':True}
    data=read(RAW+quote(path,safe='/'))
    if data.startswith(b'version https://git-lfs.github.com/spec/v1'):
        pointer=dict(line.split(' ',1) for line in data.decode().splitlines()[1:])
        size=int(pointer['size'])
        if size>160*1024*1024: raise ValueError('Unexpectedly large single art asset: '+path)
        data=read(MEDIA+quote(path,safe='/'))
        if len(data)!=size or hashlib.sha256(data).hexdigest()!=pointer['oid'].split(':')[1]:
            raise ValueError('LFS size/hash mismatch: '+path)
    target.parent.mkdir(parents=True,exist_ok=True)
    temporary=target.with_name(target.name+'.download')
    temporary.write_bytes(data); temporary.replace(target)
    print(path+' / '+str(len(data)),flush=True)
    return {'path':path,'bytes':len(data),'cached':False}

if __name__=='__main__':
    STAGE.mkdir(parents=True,exist_ok=True)
    records=[]; errors=[]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        tasks={pool.submit(fetch,path):path for path in sorted(selected)}
        for future in concurrent.futures.as_completed(tasks):
            try: records.append(future.result())
            except Exception as exc: errors.append({'path':tasks[future],'error':str(exc)})
    result={'source':'https://github.com/Unity-Technologies/FPSSample','license':'Unity Companion License; see LICENSE.md',
            'files':records,'bytes':sum(item['bytes'] for item in records),'errors':errors}
    (STAGE/'download-manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('STAGED',len(records),'files,',result['bytes'],'bytes;',len(errors),'errors',flush=True)
    if errors: raise SystemExit(1)
    earth=ROOT/'ThirdPartyStaging'/'SolarSystemScope'
    earth.mkdir(parents=True,exist_ok=True)
    for name in ['8k_earth_daymap.jpg','2k_earth_nightmap.jpg','8k_earth_clouds.jpg']:
        path=earth/name
        if not path.is_file(): path.write_bytes(read('https://www.solarsystemscope.com/textures/download/'+name))
        print('EARTH',name,path.stat().st_size,flush=True)
    (earth/'LICENSE.txt').write_text('Earth textures by Solar System Scope / INOVE.\nSource: https://www.solarsystemscope.com/textures/\nLicense: Creative Commons Attribution 4.0 International (CC BY 4.0).\nhttps://creativecommons.org/licenses/by/4.0/\nTexture files unchanged; adapted as Unity materials with custom atmosphere and lighting.\n',encoding='utf-8')
