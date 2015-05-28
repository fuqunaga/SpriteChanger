using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using AnimatorController = UnityEditorInternal.AnimatorController;
using State = UnityEditorInternal.State;


public class SpriteChanger : EditorWindow
{
    static AnimatorController src;
    static string spriteDir;
    static Dictionary<Sprite, Sprite> spriteTable = new Dictionary<Sprite, Sprite>();

    [MenuItem("Window/SpriteChanger")]
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow<SpriteChanger>();
    }


    string FullPathToAssetPath(string fullPath) {
        return "Assets" + fullPath.Replace(Application.dataPath, "").Replace("\\", "/"); 
    }

    IEnumerable<State> GetStates(AnimatorController ac)
    {
		return Enumerable.Range(0, ac.layerCount)
			.SelectMany(i => {
                var st = ac.GetLayer(i).stateMachine;
                return Enumerable.Range(0, st.stateCount)
                    .Select(st_i => st.GetState(st_i));
            });
    }

    IEnumerable<AnimationClip> GetClips(IEnumerable<State> states)
    {
        return states
                .Select(state => state.GetMotion() as AnimationClip)
                .Where(clips => clips != null);
    }

    IEnumerable<ObjectReferenceKeyframe> GetORKSprite(AnimationClip c)
    {
        return AnimationUtility.GetObjectReferenceCurveBindings(c)
                                .SelectMany(b => AnimationUtility.GetObjectReferenceCurve(c, b))
                                .Where(ork => ork.value is Sprite);
    }

    Vector2 scrollPosition;
    void OnGUI()
    {
        var oldSrc = src;
        src = EditorGUILayout.ObjectField("target", src, typeof(AnimatorController), false) as AnimatorController;

        if (src == null)
        {
            EditorGUILayout.HelpBox("Please select target AnimatorController.", MessageType.Info);
        }
        else
        {
            if (oldSrc != src) ResetSpriteTable();

            GUILayout.BeginHorizontal();
            if ( GUILayout.Button("Save"))
            {
                Save();
            }
            if ( GUILayout.Button("Save As") )
            {
                var targetDir = EditorUtility.SaveFilePanelInProject("", "", "controller", "");
                if (!string.IsNullOrEmpty(targetDir)) SaveAs(targetDir);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Search Sprite", GUILayout.Width(100)))
            {
                var dir = EditorUtility.OpenFolderPanel("Sprite Directory", spriteDir, "");
                if (!string.IsNullOrEmpty(dir))
                {
                    spriteDir = dir;
                    UpdateSpriteTable();
                }
            }
            spriteDir = EditorGUILayout.TextField(spriteDir);
            GUILayout.EndHorizontal();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            spriteTable.ToList().ForEach(pair =>
            {
                GUILayout.BeginHorizontal(GUILayout.Width(400));

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(pair.Key, typeof(Sprite), false);
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                GUILayout.Label("->");
                GUILayout.FlexibleSpace();

                spriteTable[pair.Key] = EditorGUILayout.ObjectField(pair.Value, typeof(Sprite), false) as Sprite;

                GUILayout.EndHorizontal();
            });


            GUILayout.EndScrollView();
        }
    }

    void Save()
    {
        UpdateClips(src);
        AssetDatabase.SaveAssets();
    }

    void SaveAs(string targetPath)
    {
        var success = AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), targetPath);
        if (!success) return;

        var guid = AssetDatabase.CreateFolder(Path.GetDirectoryName(targetPath), Path.GetFileNameWithoutExtension(targetPath) + "AnimationClips");
        var clipDir = AssetDatabase.GUIDToAssetPath(guid);

        success &= GetClips(GetStates(src)).All(srcClip => {
            return AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(srcClip), clipDir + "/");
        });

        if ( !success ) return;
        AssetDatabase.Refresh();

        var ac = AssetDatabase.LoadAssetAtPath(targetPath, typeof(AnimatorController)) as AnimatorController;
        GetStates(ac).ToList().ForEach(state => {
            var srcClip = state.GetMotion() as AnimationClip;
            if ( srcClip != null )
            {
                var dstPath = clipDir + "/" + Path.GetFileName(AssetDatabase.GetAssetPath(srcClip));
                var dstClip = AssetDatabase.LoadAssetAtPath(dstPath, typeof(AnimationClip)) as AnimationClip;
                state.SetAnimationClip(dstClip);
            }
        });

        EditorUtility.SetDirty(ac);

        UpdateClips(ac);

        AssetDatabase.SaveAssets();
    }

    void UpdateClips(AnimatorController ac)
    {
        GetStates(ac).ToList().ForEach(state =>
        {
            var clip = state.GetMotion() as AnimationClip;
            if ( clip != null )
            {
                AnimationUtility.GetObjectReferenceCurveBindings(clip).ToList().ForEach(binding =>
                {
                    var orks = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    for(var i=0; i<orks.Length; ++i)
                    {
                        var ork = orks[i];
                        var srcSprite = ork.value as Sprite;
                        if ( srcSprite )
                        {
                            orks[i].value = spriteTable[srcSprite];
                        }
                    }

                    AnimationUtility.SetObjectReferenceCurve(clip, binding, orks);

                    EditorUtility.SetDirty(clip);
                });
            }
        });
    }

    void ResetSpriteTable()
    {
        var states = GetStates(src);

        var clips = states
                    .Select(state => state.GetMotion() as AnimationClip)
                    .Where(ac => ac != null);

        var orkf = clips.SelectMany(c => GetORKSprite(c));

        spriteTable.Clear();
        orkf.Select(o => o.value as Sprite)
            .ToList()
            .ForEach(s => {
                if ( !spriteTable.ContainsKey(s)){
                    spriteTable.Add(s, null);
                }
            });
    }

    void UpdateSpriteTable()
    {
        var filePaths = from filePath in Directory.GetFiles(spriteDir)
                        where Path.GetExtension(filePath) != ".meta"
                        select FullPathToAssetPath(filePath);

        var nameToSprite = spriteTable.Keys.ToDictionary(sprite => sprite.name);

        filePaths.ToList().ForEach(filePath =>
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            if (nameToSprite.ContainsKey(name))
            {
                spriteTable[nameToSprite[name]] = AssetDatabase.LoadAssetAtPath(filePath, typeof(Sprite)) as Sprite;
            }
        });

    }
}
