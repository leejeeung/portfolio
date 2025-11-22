using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace jjevol
{
    public class CreateTable : EditorWindow
    {
        const string menuName = "Custom/Create Table";

        string assetPath = "Assets/ProjectArcana/Table/TSV";
        string savePath = "Assets/ProjectArcana/Table/Resources";
        bool isCreateTable = false;

        [MenuItem(menuName)]
        static void Init()
        {
            EditorWindow.GetWindow(typeof(CreateTable));
        }

        void OnGUI()
        {
            GUILayout.Label("CreateTable", EditorStyles.boldLabel);

            assetPath = EditorGUILayout.TextField("AssetPath", assetPath);
            savePath = EditorGUILayout.TextField("Build AssetBundle Path", savePath);

            if (GUILayout.Button("Create Table"))
            {
                if (!isCreateTable)
                {
                    isCreateTable = true;
                    string[] filePaths = Directory.GetFiles(savePath);
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        File.Delete(filePaths[i]);
                    }

                    filePaths = Directory.GetFiles(assetPath);
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        Debug.Log("filePaths : " + filePaths[i]);
                        if (System.IO.Path.GetExtension(filePaths[i]) == ".txt" || System.IO.Path.GetExtension(filePaths[i]) == ".tsv")
                        {
                            StringBuilder fullPath = new StringBuilder();
                            fullPath.Append(savePath);
                            fullPath.Append('/');
                            fullPath.Append(System.IO.Path.GetFileNameWithoutExtension(filePaths[i]));
                            fullPath.Append(".txt");

                            string text = System.IO.File.ReadAllText(filePaths[i]);
                            Debug.Log(JObjectUtil.TsvToJson(text));
                            byte[] encryptResult = Aes.Encrypt(System.Text.Encoding.UTF8.GetBytes(text), Aes.Type.Simple);
                            text = Convert.ToBase64String(encryptResult);

                            byte[] decryptResult = null;
                            Aes.Decrypt(Convert.FromBase64String(text), Aes.Type.Simple, out decryptResult);
                            Debug.Log(JObjectUtil.TsvToJson(System.Text.Encoding.UTF8.GetString(decryptResult)));

                            FileStream fs = File.Open(fullPath.ToString(), FileMode.Create);
                            //StreamWriter sr = System.IO.File.CreateText(fullPath.ToString());
                            StreamWriter sr = new StreamWriter(fs);
                            sr.Write(text);

                            sr.Close();
                            fs.Close();
                        }
                    }

                    isCreateTable = false;
                    Repaint();
                }
            }
        }
    }
}