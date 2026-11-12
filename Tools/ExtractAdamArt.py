"""Inspect/extract selected art from the user's acquired Unity ADAM character pack.

Unitypackage pathnames are untrusted archive data. Only safe Assets paths are read,
and only non-executable art for Adam and referenced textures is copied.
"""
import json
import sys
import tarfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path.home() / 'AppData/Roaming/Unity/Asset Store-5.x/Unity Technologies/3D ModelsCharacters/Adam Character Pack Characters.unitypackage'
DEST = ROOT / 'ThirdPartyStaging/AdamCharacterPack'

with tarfile.open(SOURCE, 'r:gz') as archive:
    members = {member.name: member for member in archive.getmembers() if member.isfile()}
    entries = {}
    for name, member in members.items():
        if name.endswith('/pathname'):
            path = archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].strip('\x00 ')
            safe = PurePosixPath(path)
            if not path.startswith('Assets/') or safe.is_absolute() or '..' in safe.parts or '\\' in path or ':' in path or '\x00' in path:
                raise ValueError('Unsafe Unity pathname: ' + path)
            entries[name.rsplit('/', 1)[0]] = path
    DEST.mkdir(parents=True, exist_ok=True)
    (DEST / 'package-paths.json').write_text(json.dumps(entries, indent=2), encoding='utf-8')
    if '--extract' in sys.argv:
        copied = []
        for guid, path in entries.items():
            ext = Path(path).suffix.lower()
            # Only ADAM is requested. Stage its source scene/controller as references;
            # the authoring helper imports neither scene nor upstream game scripts.
            if not ('/Adam/' in path or path.endswith('/license.txt') or path.endswith('/Scenes/Adam_Scene.unity')):
                continue
            if ext not in {'.fbx', '.tga', '.png', '.jpg', '.tif', '.psd', '.mat', '.anim', '.txt', '.md', '.controller', '.unity'}:
                continue
            target = DEST / path
            target.parent.mkdir(parents=True, exist_ok=True)
            for suffix, destination in [('asset', target), ('asset.meta', Path(str(target) + '.meta'))]:
                member = members.get(guid + '/' + suffix)
                if member:
                    destination.write_bytes(archive.extractfile(member).read())
            copied.append(path)
        (DEST / 'extracted.json').write_text(json.dumps(copied, indent=2), encoding='utf-8')
        print('Extracted', len(copied), 'art/notice files')
    else:
        for path in entries.values():
            print(path)
