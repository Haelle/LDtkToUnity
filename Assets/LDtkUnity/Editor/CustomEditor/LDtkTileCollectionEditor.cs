﻿using UnityEditor;
using UnityEngine;

namespace LDtkUnity.Editor
{
    [CustomEditor(typeof(LDtkArtifactAssets))]
    public class LDtkTileCollectionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            SerializedProperty arrayProp = serializedObject.FindProperty(LDtkArtifactAssets.PROP_TILE_LIST);

            if (arrayProp == null)
            {
                return;
            }
            
            GUIContent content = new GUIContent($"{arrayProp.arraySize} Tiles");
            EditorGUILayout.LabelField(content);
            LDtkEditorGUIUtility.DrawDivider();

            if (arrayProp.arraySize > 1500)
            {
                EditorGUILayout.LabelField("(Too many tiles to display)");
                return;
            }

            Texture image = LDtkIconUtility.GetUnityIcon("Tile");
            
            EditorGUIUtility.SetIconSize(Vector2.one * 16);
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                SerializedProperty tileProp = arrayProp.GetArrayElementAtIndex(i);

                if (tileProp == null || tileProp.objectReferenceValue == null)
                {
                    Debug.LogError("TileCollection drawer error");
                    return;
                }
                
                GUIContent tileContent = new GUIContent()
                {
                    text = tileProp.objectReferenceValue.name, 
                    image = image
                };
                
                EditorGUILayout.LabelField(tileContent);
            }
        }
    }
}