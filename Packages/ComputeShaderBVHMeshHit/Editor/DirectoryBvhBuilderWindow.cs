using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;


namespace ComputeShaderBvhMeshHit.Editor
{
    public class DirectoryBvhBuilderWindow : EditorWindow
    {
        public int splitCount = 64;

        string lastPath;

        string dir;

        [MenuItem("Window/DirectoryBvhBuilder")]
        static void Init()
        {
            var window = GetWindowWithRect<DirectoryBvhBuilderWindow>(new Rect(0f, 0f, 400f, 130f));
            window.Show();
        }

        void OnGUI()
        {
            GUILayout.Space(32f);

            dir = EditorGUILayout.TextField("文件夹路径" ,dir);
            
            splitCount = EditorGUILayout.IntField(nameof(splitCount), splitCount);

            GUILayout.Space(32f);
            DirectoryInfo directoryInfo = new DirectoryInfo(dir);
            GUI.enabled = directoryInfo.Exists && (dir != null) && (splitCount > 0);
            if (GUILayout.Button("Build"))
            {

                foreach (var f in directoryInfo.GetFiles("*.prefab" ,SearchOption.AllDirectories))
                {
                    var directory = "Assets";
                    var defaultName = "bvhAsset";
                    if ( !string.IsNullOrEmpty(lastPath))
                    {
                        directory = Path.GetDirectoryName(lastPath);
                        defaultName = Path.GetFileName(lastPath);
                    }

                    //var path = EditorUtility.SaveFilePanel("Save Bvh asset",  directory, defaultName, "asset");
                    var meshObjectRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ToLocalPath(f.FullName));
                    var meshObjectRootPath = AssetDatabase.GetAssetPath(meshObjectRoot);
                    FileInfo meshObjectRootFileInfo = new FileInfo(meshObjectRootPath);

                    var path = meshObjectRootFileInfo.Directory.FullName + "/" + meshObjectRootFileInfo.Name.Split('.').First() + ".asset";
                    if (!string.IsNullOrEmpty(path))
                    {
                        lastPath = path;
                        var relativePath = "Assets" + path.Substring(Application.dataPath.Length);

                        var (bvhDatas, triangles) = BvhBuilder.BuildBvh(meshObjectRoot, splitCount);

                        var bvhAsset = AssetDatabase.LoadAssetAtPath<BvhAsset>(relativePath);
                        if (bvhAsset == null)
                        {
                            bvhAsset = CreateInstance<BvhAsset>();
                            AssetDatabase.CreateAsset(bvhAsset, relativePath);
                        }
                        bvhAsset.bvhDatas = bvhDatas;
                        bvhAsset.triangles = triangles;

                        EditorUtility.SetDirty(bvhAsset);
                        AssetDatabase.SaveAssets();
                        EditorGUIUtility.PingObject(bvhAsset);
                    }
                }
                
                
            }
        }

        string ToLocalPath(string globalPath)
        {
            var mp = globalPath.Substring(globalPath.IndexOf("Assets"));
            return mp.Replace('\\', '/');
        }
        
    }
}