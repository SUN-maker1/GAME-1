import uuid, random, os, shutil, re

ROOT = r"D:/youxicongtoulai/My project (1)/Assets"
KN = os.path.join(ROOT, "IMAGE", "Knight")
TEX_GUID = "6fc20d68d9cc8124389a0d35b94a9369"   # keep Unity-generated guid for knight.png

CW, CH, COLS, ROWS = 48, 60, 4, 2
SHEET_W, SHEET_H = CW * COLS, CH * ROWS

def rid():      # random int64 (negative), as Unity internalID
    return -(random.randint(10**18, 9 * 10**18))

def rguid():
    return uuid.uuid4().hex

# ---------- 1. final png ----------
shutil.copy(os.path.join(KN, "knight_f4.png"), os.path.join(KN, "knight.png"))
for f in list(os.listdir(KN)):
    if re.match(r"knight_(F\d|f\d+|full|mid|med|clean)\.png$", f) or f.endswith(".png.meta") and re.match(r"knight_(F\d|f\d+|full|mid|med|clean)\.png\.meta$", f):
        os.remove(os.path.join(KN, f))
print("pngs:", os.listdir(KN))

# ---------- 2. sprite table ----------
sprites = []
for row, prefix in enumerate(["knight_idle", "knight_walk"]):
    for col in range(COLS):
        sprites.append({
            "name": f"{prefix}_{col}",
            "x": col * CW,
            "y": (ROWS - 1 - row) * CH,   # unity y origin = bottom
            "id": rid(),
            "sid": rguid(),
        })

tbl = "".join(
    f"  - first:\n      213: {s['id']}\n    second: {s['name']}\n" for s in sprites)
nft = "".join(f"      {s['name']}: {s['id']}\n" for s in sprites)
entries = ""
for s in sprites:
    entries += (
        f"    - serializedVersion: 2\n"
        f"      name: {s['name']}\n"
        f"      rect:\n"
        f"        serializedVersion: 2\n"
        f"        x: {s['x']}\n"
        f"        y: {s['y']}\n"
        f"        width: {CW}\n"
        f"        height: {CH}\n"
        f"      alignment: 0\n"
        f"      pivot: {{x: 0.5, y: 0.5}}\n"
        f"      border: {{x: 0, y: 0, z: 0, w: 0}}\n"
        f"      outline: []\n"
        f"      physicsShape: []\n"
        f"      tessellationDetail: 0\n"
        f"      bones: []\n"
        f"      spriteID: {s['sid']}\n"
        f"      internalID: {s['id']}\n"
        f"      vertices: []\n"
        f"      indices: \n"
        f"      edges: []\n"
        f"      weights: []\n")

meta = f"""fileFormatVersion: 2
guid: {TEX_GUID}
TextureImporter:
  internalIDToNameTable:
{tbl}  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 2
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 32
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: WebGL
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites:
{entries}    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable:
{nft.rstrip()}
  mipmapLimitGroupName: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
with open(os.path.join(KN, "knight.png.meta"), "w", encoding="utf-8", newline="\n") as f:
    f.write(meta)
print("knight.png.meta written,", len(sprites), "sprites")

# ---------- 3. anim clips ----------
def anim_clip(name, ids, fps, stop):
    keys = "".join(
        f"    - time: {i / fps:.8f}\n      value: {{fileID: {s['id']}, guid: {TEX_GUID}, type: 3}}\n"
        for i, s in enumerate(ids))
    mapping = "".join(f"    - {{fileID: {s['id']}, guid: {TEX_GUID}, type: 3}}\n" for s in ids)
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  serializedVersion: 7
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves: []
  m_FloatCurves: []
  m_PPtrCurves:
  - curve:
{keys}    path: 
  attribute: m_Sprite
  path: 
  classID: 212
  script: {{fileID: 0}}
  m_SampleRate: {fps}
  m_WrapMode: 2
  m_Bounds:
    m_Center: {{x: 0, y: 0, z: 0}}
    m_Extent: {{x: 0, y: 0, z: 0}}
  m_ClipBindingConstant:
    genericBindings:
    - serializedVersion: 2
      path: 0
      attribute: 0
      script: {{fileID: 0}}
      typeID: 212
      customType: 23
      isPPtrCurve: 1
      pptrParameter: 0
    pptrCurveMapping:
{mapping}  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {{fileID: 0}}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: {stop:.8f}
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: 1
    m_LoopBlend: 0
    m_LoopBlendOrientation: 0
    m_LoopBlendPositionY: 0
    m_LoopBlendPositionXZ: 0
    m_KeepOriginalOrientation: 0
    m_KeepOriginalPositionY: 1
    m_KeepOriginalPositionXZ: 0
    m_HeightFromFeet: 0
    m_Mirror: 0
  m_EditorCurves: []
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events: []
"""

def anim_meta(guid):
    return (f"fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n"
            "  externalObjects: {}\n  mainObjectFileID: 7400000\n  userData: \n"
            "  assetBundleName: \n  assetBundleVariant: \n")

idle_guid, walk_guid = rguid(), rguid()
with open(os.path.join(KN, "Knight_Idle.anim"), "w", encoding="utf-8", newline="\n") as f:
    f.write(anim_clip("Knight_Idle", sprites[0:4], 6, 4 / 6))
with open(os.path.join(KN, "Knight_Idle.anim.meta"), "w", encoding="utf-8", newline="\n") as f:
    f.write(anim_meta(idle_guid))
with open(os.path.join(KN, "Knight_Walk.anim"), "w", encoding="utf-8", newline="\n") as f:
    f.write(anim_clip("Knight_Walk", sprites[4:8], 8, 0.5))
with open(os.path.join(KN, "Knight_Walk.anim.meta"), "w", encoding="utf-8", newline="\n") as f:
    f.write(anim_meta(walk_guid))
print("anim clips written, guids:", idle_guid, walk_guid)

# ---------- 4. animator controller (reuses Square.controller guid) ----------
IDLE_STATE, WALK_STATE = 110200000, 110200001
T_IW, T_WI = 110100000, 110100001
SM = 110700000
controller = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Square
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Speed
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: 0}}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {{fileID: {SM}}}
    m_Mask: {{fileID: 0}}
--- !u!1107 &{SM}
AnimatorStateMachine:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {{fileID: {IDLE_STATE}}}
    m_Position: {{x: 220, y: 20, z: 0}}
  - serializedVersion: 1
    m_State: {{fileID: {WALK_STATE}}}
    m_Position: {{x: 460, y: 20, z: 0}}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {{}}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {{x: 50, y: 20, z: 0}}
  m_EntryPosition: {{x: 50, y: 120, z: 0}}
  m_ExitPosition: {{x: 800, y: 120, z: 0}}
  m_ParentStateMachinePosition: {{x: 800, y: 20, z: 0}}
  m_DefaultState: {{fileID: {IDLE_STATE}}}
--- !u!1101 &{T_IW}
AnimatorStateTransition:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: 
  m_Conditions:
  - m_ConditionMode: 3
    m_ConditionEvent: Speed
    m_EventTreshold: 0.1
  m_DstStateMachine: {{fileID: 0}}
  m_DstState: {{fileID: {WALK_STATE}}}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0.05
  m_TransitionOffset: 0
  m_ExitTime: 0.75
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1101 &{T_WI}
AnimatorStateTransition:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: 
  m_Conditions:
  - m_ConditionMode: 4
    m_ConditionEvent: Speed
    m_EventTreshold: 0.1
  m_DstStateMachine: {{fileID: 0}}
  m_DstState: {{fileID: {IDLE_STATE}}}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0.05
  m_TransitionOffset: 0
  m_ExitTime: 0.75
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1102 &{IDLE_STATE}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Idle
  m_Speed: 1
  m_SpeedParameter: 
  m_SpeedParameterActive: 0
  m_MirrorParameter: 
  m_CycleOffsetParameter: 
  m_TimeParameter: 
  m_Motion: {{fileID: 7400000, guid: {idle_guid}, type: 2}}
  m_Tag: 
  m_Transitions:
  - {{fileID: {T_IW}}}
  m_StateMachineBehaviours: []
  m_Position: {{x: 220, y: 20, z: 0}}
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
--- !u!1102 &{WALK_STATE}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Walk
  m_Speed: 1
  m_SpeedParameter: 
  m_SpeedParameterActive: 0
  m_MirrorParameter: 
  m_CycleOffsetParameter: 
  m_TimeParameter: 
  m_Motion: {{fileID: 7400000, guid: {walk_guid}, type: 2}}
  m_Tag: 
  m_Transitions:
  - {{fileID: {T_WI}}}
  m_StateMachineBehaviours: []
  m_Position: {{x: 460, y: 20, z: 0}}
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
"""
with open(os.path.join(ROOT, "IMAGE", "Square.controller"), "w", encoding="utf-8", newline="\n") as f:
    f.write(controller)
print("Square.controller rewritten")
