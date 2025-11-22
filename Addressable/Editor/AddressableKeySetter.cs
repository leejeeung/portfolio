// Editor/AddressableKeySetter.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace jjevol.Addressing.EditorTools
{
    /// <summary>
    /// 지정 루트 폴더를 스캔하여 Addressables "체크 + 키(address) 설정"을 수행하는 툴.
    ///
    /// 규칙:
    ///   Assets/ProjectArcana/Addressables/Local/<Top>/A/B/File.ext
    ///     → 그룹: "<Top>"
    ///     → 키:   "A/B/File" (확장자 제외)
    ///
    ///   Assets/ProjectArcana/Addressables/Remote/<Top>/A/B/File.ext
    ///     → 그룹: "Remote_<Top>" (groupHasRemotePrefix=true 인 경우)
    ///     → 키:   "A/B/File"
    ///
    /// 옵션:
    ///   - Auto Add If Not Addressable : Addressables에 등록되지 않은 파일이면 자동 등록(체크)
    ///   - Create Group If Missing     : 대상 그룹이 없으면 자동 생성
    ///   - Only If Empty               : 기존 address가 있으면 보존
    ///   - Include Extension           : 키에 확장자 포함
    ///   - Ensure Unique               : 전역 중복 시 #HASH 접미어로 충돌 회피
    ///   - Groups use 'Remote_<Top>'   : Remote 그룹명에 Remote_ 접두 사용
    /// </summary>
    public class AddressableKeySetter : EditorWindow
    {
        private const string RootDefault = "Assets/ProjectArcana/Addressables";

        [SerializeField] private string rootPath = RootDefault;

        [Header("Registration")]
        [SerializeField] private bool autoAddIfNotAddressable = true;  // ✅ 새로 체크
        [SerializeField] private bool createGroupIfMissing = true;      // ✅ 그룹 자동 생성

        [Header("Key Options")]
        [SerializeField] private bool onlyIfEmpty = false;
        [SerializeField] private bool includeExtension = false;
        [SerializeField] private bool ensureUnique = false;

        // 일부 프로젝트는 그룹명이 "Remote_<Top>"가 아니라 "<Top>"인 경우도 있으니 보정 옵션 제공
        [SerializeField] private bool groupHasRemotePrefix = true; // 그룹명이 Remote_<Top> 형태면 true

        [MenuItem("Custom/Addressables/Set Keys (Include Auto-Check)")]
        public static void Open() => GetWindow<AddressableKeySetter>("Set Keys + Auto Check");

        private void OnGUI()
        {
            GUILayout.Label("Addressable Auto-Check + Key Setter", EditorStyles.boldLabel);
            rootPath = EditorGUILayout.TextField("Root Path", rootPath);
            groupHasRemotePrefix = EditorGUILayout.Toggle("Groups use 'Remote_<Top>' format", groupHasRemotePrefix);

            GUILayout.Space(8);
            GUILayout.Label("Registration", EditorStyles.boldLabel);
            autoAddIfNotAddressable = EditorGUILayout.Toggle("Auto Add If Not Addressable", autoAddIfNotAddressable);
            createGroupIfMissing = EditorGUILayout.Toggle("Create Group If Missing", createGroupIfMissing);

            GUILayout.Space(8);
            GUILayout.Label("Key Options", EditorStyles.boldLabel);
            onlyIfEmpty = EditorGUILayout.Toggle("Only If Empty", onlyIfEmpty);
            includeExtension = EditorGUILayout.Toggle("Include Extension", includeExtension);
            ensureUnique = EditorGUILayout.Toggle("Ensure Unique (#HASH if collision)", ensureUnique);

            GUILayout.Space(12);
            if (GUILayout.Button("Run")) Run();

            EditorGUILayout.HelpBox(
                "예시)\n" +
                "Local/Sound/Hit/Knife_Hermit.prefab → 그룹=Sound → 키=Hit/Knife_Hermit\n" +
                "Remote/Sound/BGM/Stage01.asset     → 그룹=Remote_Sound → 키=BGM/Stage01\n\n" +
                "체크 안 된 파일도 자동으로 Addressables에 등록되고, 그룹이 없으면 생성됩니다.",
                MessageType.Info);
        }

        private void Run()
        {
            if (!Directory.Exists(rootPath))
            {
                Debug.LogError("Root not found: " + rootPath);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables settings not found.");
                return;
            }

            // 현재 모든 address를 수집(전역 중복 방지용)
            var existingAddresses = new HashSet<string>(
                settings.groups.Where(g => g != null)
                               .SelectMany(g => g.entries)
                               .Where(e => e != null && !string.IsNullOrEmpty(e.address))
                               .Select(e => e.address)
            );

            var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
                                 .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                                 .Select(p => p.Replace("\\", "/"))
                                 .ToList();

            int updatedKeys = 0;
            int addedEntries = 0;
            var touched = new List<AddressableAssetEntry>();
            var createdGroups = new HashSet<string>();

            foreach (var absPath in files)
            {
                string assetPath = absPath; // already normalized to '/'
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                // Local/Remote 및 Top 추출
                if (!TryParseRootRelative(assetPath, out var isLocal, out var isRemote, out var top, out var afterTopWithExt))
                {
                    // root 범위밖이거나 예외 구조면 스킵
                    continue;
                }

                // 대상 그룹명 결정
                string groupName = ResolveGroupName(isLocal, isRemote, top);

                // 현재 Addressables 엔트리 조회
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);

                // 엔트리가 없고, 자동 등록 옵션일 때 → 그룹 찾아 추가
                if (entry == null && autoAddIfNotAddressable)
                {
                    var group = FindOrCreateGroup(settings, groupName, createGroupIfMissing, createdGroups);
                    if (group == null)
                    {
                        Debug.LogWarning($"[AddressableKeySetter] Group not found and auto-create disabled: {groupName} (skip)  ({assetPath})");
                        continue;
                    }

                    entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                    if (entry != null) addedEntries++;
                }

                // 여전히 엔트리가 없으면(자동등록 비활성/그룹 없음 등) → 스킵
                if (entry == null) continue;

                // 새 키 생성
                string newKey = MakeKeyFromAfterTop(afterTopWithExt);

                // OnlyIfEmpty 옵션 처리
                if (onlyIfEmpty && !string.IsNullOrEmpty(entry.address))
                {
                    continue; // 기존 키 보존
                }

                // 전역 충돌 방지
                if (ensureUnique)
                    newKey = EnsureUniqueAddress(newKey, absPath, existingAddresses);

                if (entry.address != newKey)
                {
                    entry.SetAddress(newKey);
                    existingAddresses.Add(newKey);
                    updatedKeys++;
                    touched.Add(entry);
                }
            }

            // 더티 플래그 반영
            if (touched.Count > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, touched, true, false);
                foreach (var g in settings.groups)
                    g?.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, touched, false, true);
            }

            if (createdGroups.Count > 0)
            {
                // 그룹 생성시에도 세팅 더티
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupAdded, settings.groups, true, true);
            }

            Debug.Log($"[AddressableKeySetter] Added {addedEntries} entries, Updated {updatedKeys} addresses. Groups created: {createdGroups.Count}");
        }

        // ─────────────────────────────────────────────────────────────
        // 경로 파싱 & 그룹명/키 생성
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// rootPath 기준으로 Local/Remote, Top, Top 이후 경로(+확장자)를 파싱.
        /// </summary>
        private bool TryParseRootRelative(string absPath, out bool isLocal, out bool isRemote, out string top, out string afterTopWithExt)
        {
            isLocal = false; isRemote = false; top = string.Empty; afterTopWithExt = string.Empty;

            var rel = GetRelative(absPath, rootPath); // e.g. "Local/Sound/Hit/Knife_Hermit.prefab"
            if (string.IsNullOrEmpty(rel)) return false;

            rel = rel.Replace("\\", "/");

            if (rel.StartsWith("Local/", StringComparison.OrdinalIgnoreCase))
            {
                isLocal = true;
                rel = rel.Substring("Local/".Length); // "Sound/Hit/Knife_Hermit.prefab"
            }
            else if (rel.StartsWith("Remote/", StringComparison.OrdinalIgnoreCase))
            {
                isRemote = true;
                rel = rel.Substring("Remote/".Length); // "Sound/BGM/Stage01.asset"
            }
            else
            {
                // Local/Remote 분기가 없는 루트일 수도 있다 → Top만 있는 구조로 간주
            }

            var parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            top = parts[0]; // "Sound"
            afterTopWithExt = parts.Length >= 2 ? string.Join("/", parts.Skip(1)) : Path.GetFileName(absPath);
            return true;
        }

        private string ResolveGroupName(bool isLocal, bool isRemote, string top)
        {
            if (string.IsNullOrEmpty(top)) return string.Empty;

            if (isRemote && groupHasRemotePrefix)
                return $"Remote_{top}";
            return top;
        }

        /// <summary>
        /// afterTop 경로("A/B/File.ext")로부터 address 키 생성(확장자 옵션 반영)
        /// </summary>
        private string MakeKeyFromAfterTop(string afterTopWithExt)
        {
            string key = afterTopWithExt;
            if (!includeExtension)
            {
                var ext = Path.GetExtension(key);
                if (!string.IsNullOrEmpty(ext))
                    key = key.Substring(0, key.Length - ext.Length);
            }
            if (string.IsNullOrEmpty(key))
                key = "Unnamed";
            return key.Replace("\\", "/");
        }

        private AddressableAssetGroup FindOrCreateGroup(AddressableAssetSettings settings, string groupName, bool createIfMissing, HashSet<string> createdGroups)
        {
            if (string.IsNullOrEmpty(groupName)) return null;

            var group = settings.groups.FirstOrDefault(g => g != null && g.Name == groupName);
            if (group != null) return group;

            if (!createIfMissing) return null;

            group = settings.CreateGroup(groupName, false, false, false, null);
            if (group != null)
            {
                createdGroups.Add(groupName);
            }
            return group;
        }

        // ───────────── 유틸 ─────────────
        private static string GetRelative(string path, string root)
        {
            var p = path.Replace("\\", "/");
            var r = (root ?? string.Empty).Replace("\\", "/").TrimEnd('/');
            return p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase) ? p.Substring(r.Length + 1) : p;
        }

        private static string EnsureUniqueAddress(string baseKey, string absPath, HashSet<string> existing)
        {
            if (!existing.Contains(baseKey)) return baseKey;

            // 충돌 → 최소 해시 부여
            string withHash = $"{baseKey}#{ShortHash(absPath)}";
            if (!existing.Contains(withHash)) return withHash;

            // 혹시 또 충돌하면 숫자 접미로 회피
            int i = 2;
            string alt;
            do { alt = $"{withHash}_{i++}"; }
            while (existing.Contains(alt));
            return alt;
        }

        private static string ShortHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "0000";
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var raw = System.Text.Encoding.UTF8.GetBytes(input);
                var h = sha1.ComputeHash(raw);
                return BitConverter.ToString(h, 0, 4).Replace("-", "");
            }
        }
    }
}
#endif
