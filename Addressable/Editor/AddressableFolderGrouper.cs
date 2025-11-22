// Editor/AddressableFolderGrouper.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace jjevol.Addressing.EditorTools
{
    /// <summary>
    /// Folder 규칙으로 그룹 생성/이동 + 주소 설정:
    /// - Assets/ProjectArcana/Addressables/Local/<Top>/**  -> 그룹 "<Top>"  (Local 스키마)
    /// - Assets/ProjectArcana/Addressables/Remote/<Top>/** -> 그룹 "Remote_<Top>" (Remote 스키마)
    /// - 루트 바로 아래 파일(Assets/ProjectArcana/Addressables/*.*) -> 그룹 "Local"
    /// 주소는 파일명(확장자 제외). 그룹 내 중복 시 "#{HASH}" 접미사 부여.
    /// (옵션) 폴더 라벨 부여.
    /// </summary>
    public class AddressableFolderGrouper : EditorWindow
    {
        private const string Menu = "Custom/Addressables/Group By Folder Convention";
        [MenuItem(Menu)]
        public static void Open() => GetWindow<AddressableFolderGrouper>("Group By Folder");

        [SerializeField] private string rootPath = "Assets/ProjectArcana/Addressables";
        [SerializeField] private bool writeFolderLabels = true;
        [SerializeField] private int labelDepth = 2;

        private Vector2 _scroll;

        private void OnGUI()
        {
            GUILayout.Label("Group by Folder Convention", EditorStyles.boldLabel);
            rootPath = EditorGUILayout.TextField("Root Path", rootPath);
            writeFolderLabels = EditorGUILayout.Toggle("Write Folder Labels", writeFolderLabels);
            if (writeFolderLabels) labelDepth = EditorGUILayout.IntSlider("Label Depth", labelDepth, 1, 4);

            if (GUILayout.Button("Run")) Run();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                "Rules:\n" +
                "• Local/<Top> → group '<Top>' (Local schema)\n" +
                "• Remote/<Top> → group 'Remote_<Top>' (Remote schema)\n" +
                "• Files directly under root → group 'Local'\n\n" +
                "Schemas follow your screenshots:\n" +
                "Local: IncludeInBuild=ON, InternalAssetNaming=Filename, InternalBundleId=GroupGuid, BundleMode=PackSeparately, BundleNaming=Filename\n" +
                "Remote: IncludeInBuild=ON, InternalAssetNaming=FullPath, InternalBundleId=GroupGuidProjectIdHash, BundleMode=PackSeparately, BundleNaming=Filename",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables settings not found.");
                return;
            }
            if (!Directory.Exists(rootPath))
            {
                Debug.LogError("Root not found: " + rootPath);
                return;
            }

            int moved = 0;
            var touched = new List<AddressableAssetEntry>();

            // 1) 루트 바로 아래 파일 → 그룹 "Local"
            string[] rootFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly);
            var localRootGroup = GetOrCreateGroup(settings, "Local", isRemote: false);
            moved += ProcessFiles(settings, localRootGroup, rootFiles, rootPath, touched, writeFolderLabels, labelDepth);

            // 2) Local/<Top>/**
            string localDir = Path.Combine(rootPath, "Local").Replace("\\", "/");
            if (Directory.Exists(localDir))
            {
                foreach (var top in Directory.GetDirectories(localDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var topName = Path.GetFileName(top);
                    var group = GetOrCreateGroup(settings, topName, isRemote: false);
                    moved += ProcessTree(settings, group, top, localDir, touched, writeFolderLabels, labelDepth);
                }
            }

            // 3) Remote/<Top>/**
            string remoteDir = Path.Combine(rootPath, "Remote").Replace("\\", "/");
            if (Directory.Exists(remoteDir))
            {
                foreach (var top in Directory.GetDirectories(remoteDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var topName = Path.GetFileName(top);
                    var group = GetOrCreateGroup(settings, "Remote_" + topName, isRemote: true);
                    moved += ProcessTree(settings, group, top, remoteDir, touched, writeFolderLabels, labelDepth);
                }
            }

            if (touched.Count > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, touched, true, false);
                foreach (var g in settings.groups)
                    g.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, touched, false, true);
            }

            Debug.Log($"[FolderGrouper] Done. Entries moved/added: {moved}");
        }

        private int ProcessTree(AddressableAssetSettings settings, AddressableAssetGroup group, string startDir, string labelRoot, List<AddressableAssetEntry> touched, bool writeLabels, int depth)
        {
            int moved = 0;

            // 현재 폴더의 파일 처리
            var files = Directory.GetFiles(startDir, "*.*", SearchOption.TopDirectoryOnly);
            moved += ProcessFiles(settings, group, files, labelRoot, touched, writeLabels, depth);

            // 하위 폴더 재귀
            foreach (var dir in Directory.GetDirectories(startDir, "*", SearchOption.TopDirectoryOnly))
                moved += ProcessTree(settings, group, dir, labelRoot, touched, writeLabels, depth);

            return moved;
        }

        private int ProcessFiles(AddressableAssetSettings settings, AddressableAssetGroup group, string[] files, string labelRoot, List<AddressableAssetEntry> touched, bool writeLabels, int depth)
        {
            int moved = 0;
            foreach (var abs in files)
            {
                var ext = Path.GetExtension(abs).ToLowerInvariant();
                if (ext == ".meta") continue;

                var assetPath = abs.Replace("\\", "/");
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);

                // 주소 = 파일명 (확장자 제외), 중복 시 #HASH
                var fileName = Path.GetFileNameWithoutExtension(assetPath);
                string address = fileName;
                var relForHash = GetRelative(assetPath, "Assets/ProjectArcana/Addressables");
                string hash = ShortHash(relForHash);
                if (GroupHasAddress(group, address, entry))
                    address = $"{fileName}#{hash}";
                entry.SetAddress(address);
                touched.Add(entry);
                moved++;

                // 라벨
                if (writeLabels)
                {
                    var relFromLabelRoot = GetRelative(assetPath, labelRoot);
                    var parts = relFromLabelRoot.Split('/');
                    int max = Mathf.Min(depth, Mathf.Max(0, parts.Length - 1)); // 파일명 제외
                    for (int i = 0; i < max; i++)
                    {
                        var label = parts[i];
                        if (!string.IsNullOrEmpty(label))
                        {
                            settings.AddLabel(label);
                            entry.SetLabel(label, true);
                        }
                    }
                }
            }
            return moved;
        }

        private static string GetRelative(string path, string root)
        {
            var p = path.Replace("\\", "/");
            var r = root.Replace("\\", "/").TrimEnd('/');
            if (p.StartsWith(r + "/")) return p.Substring(r.Length + 1);
            return p;
        }

        private static bool GroupHasAddress(AddressableAssetGroup group, string address, AddressableAssetEntry exclude = null)
        {
            // 같은 그룹 내 동일 address 존재 여부
            foreach (var e in group.entries)
            {
                if (e == exclude) continue;
                if (e != null && e.address == address) return true;
            }
            return false;
        }

        private AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName, bool isRemote)
        {
            var group = settings.FindGroup(groupName);
            if (group == null)
                group = settings.CreateGroup(groupName, false, false, true, null, typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));

            var bundled = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            var contentUpdate = group.GetSchema<ContentUpdateGroupSchema>() ?? group.AddSchema<ContentUpdateGroupSchema>();

            // 프로필 변수명: LocalBuildPath/LocalLoadPath, RemoteBuildPath/RemoteLoadPath 가 존재해야 함
            bundled.BuildPath.SetVariableByName(settings, isRemote ? "RemoteBuildPath" : "LocalBuildPath");
            bundled.LoadPath.SetVariableByName(settings, isRemote ? "RemoteLoadPath" : "LocalLoadPath");

            // 공통
            bundled.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundled.IncludeInBuild = true;
            bundled.UseAssetBundleCache = false;
            bundled.UseAssetBundleCrc = false;
            bundled.UseUnityWebRequestForLocalBundles = false;
            bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            bundled.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.NoHash;
            bundled.AssetLoadMode = AssetLoadMode.RequestedAssetAndDependencies;
            bundled.AssetBundledCacheClearBehavior = BundledAssetGroupSchema.CacheClearBehavior.ClearWhenSpaceIsNeededInCache;

            // 스크린샷에 맞춘 차이
            bundled.InternalIdNamingMode = isRemote
                ? BundledAssetGroupSchema.AssetNamingMode.FullPath
                : BundledAssetGroupSchema.AssetNamingMode.Filename;

            bundled.InternalBundleIdMode = isRemote
                ? BundledAssetGroupSchema.BundleInternalIdMode.GroupGuidProjectIdHash
                : BundledAssetGroupSchema.BundleInternalIdMode.GroupGuid;

            // Prevent Updates = OFF (콘텐츠 업데이트/외부 카탈로그로 교체 가능)
            contentUpdate.StaticContent = false;

            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(bundled);
            EditorUtility.SetDirty(contentUpdate);
            return group;
        }

        private static string ShortHash(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(input);
                var hash = md5.ComputeHash(bytes);
                // 앞 4바이트만 축약 표시
                return System.BitConverter.ToString(hash, 0, 4).Replace("-", "");
            }
        }
    }
}
#endif
