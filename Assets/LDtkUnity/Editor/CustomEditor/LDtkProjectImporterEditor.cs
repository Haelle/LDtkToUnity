﻿using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LDtkUnity.Editor
{
    [CustomEditor(typeof(LDtkProjectImporter))]
    public class LDtkProjectImporterEditor : LDtkJsonImporterEditor
    {
        private LdtkJson _data;
        
        private ILDtkSectionDrawer[] _sectionDrawers;
        
        private ILDtkSectionDrawer _sectionLevels;
        private ILDtkSectionDrawer _sectionIntGrids;
        private ILDtkSectionDrawer _sectionEntities;
        private ILDtkSectionDrawer _sectionEnums;
        private ILDtkSectionDrawer _sectionGridPrefabs;

        private bool _levelFieldsError;
        private bool _isFirstUpdate = true;

        private static readonly GUIContent PixelsPerUnit = new GUIContent
        {
            text = "Main Pixels Per Unit",
            tooltip = "Dictates what all of the instantiated Tileset scales will adjust to, in case several LDtk layer's GridSize's are different."
        };

        private static readonly GUIContent DeparentInRuntime = new GUIContent
        {
            text = "De-parent in Runtime",
            tooltip = "When on, adds components to the project, levels, and entity-layer GameObjects that act to de-parent all of their children in runtime.\n" +
                      "This results in increased runtime performance.\n" +
                      "Keep this on if the exact level/layer hierarchy structure is not a concern in runtime."
                
        };
        
        private static readonly GUIContent LogBuildTimes = new GUIContent
        {
            text = "Log Build Times",
            tooltip = "Use this to display the count of levels built, and how long it took to generate them."
        };
        
        private static readonly GUIContent LevelFields = new GUIContent
        {
            text = "Level Fields Prefab",
            tooltip = "This field stores a prefab that would have a script for field injection, exactly like entities.\n" +
                      "This prefab is instantiated as the root GameObject for all levels in the build process."
                
        };
        
        public override bool showImportedObject => false;

        public override void OnEnable()
        {
            base.OnEnable();
            
            _sectionLevels = new LDtkSectionLevels(serializedObject);
            _sectionIntGrids = new LDtkSectionIntGrids(serializedObject);
            _sectionEntities = new LDtkSectionEntities(serializedObject);
            _sectionEnums = new LDtkSectionEnums(serializedObject);
            _sectionGridPrefabs = new LDtkSectionGridPrefabs(serializedObject);
            
            _sectionDrawers = new[]
            {
                _sectionLevels,
                _sectionIntGrids,
                _sectionEntities,
                _sectionEnums,
                _sectionGridPrefabs
            };

            foreach (ILDtkSectionDrawer drawer in _sectionDrawers)
            {
                drawer.Init();
            }
            
        }

        public override void OnDisable()
        {
            foreach (ILDtkSectionDrawer drawer in _sectionDrawers)
            {
                drawer.Dispose();
            }
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            try
            {
                //at the start of all drawing, set icon size for some GuiContents
                EditorGUIUtility.SetIconSize(Vector2.one * 16);
            
                serializedObject.Update();
                ShowGUI();
                serializedObject.ApplyModifiedProperties();
            
                EditorGUIUtility.SetIconSize(Vector2.one * 32);

                if (_isFirstUpdate)
                {
                    ApplyIfArraySizesChanged();
                    _isFirstUpdate = false;
                }
                DrawPotentialProblem();
            }
            finally
            {
                ApplyRevertGUI();
            }
        }

        private void ShowGUI()
        {
            if (!AssignJsonField() || _data == null)
            {
                return;
            }

            Definitions defs = _data.Defs;
            
            DrawField(PixelsPerUnit, LDtkProjectImporter.PIXELS_PER_UNIT);
            DrawField(DeparentInRuntime, LDtkProjectImporter.DEPARENT_IN_RUNTIME);
            DrawField(LogBuildTimes, LDtkProjectImporter.LOG_BUILD_TIMES);
            
            _levelFieldsError = LevelFieldsPrefabField(defs.LevelFields);

            
            _sectionLevels.Draw(_data.Levels);
            //_sectionLevelBackgrounds.Draw(_data.Levels.Where(level => !string.IsNullOrEmpty(level.BgRelPath)));
            _sectionIntGrids.Draw(defs.IntGridLayers);
            _sectionEntities.Draw(defs.Entities);
            _sectionEnums.Draw(defs.Enums);
            //_sectionTilesets.Draw(defs.Tilesets);
            //_sectionTileAssets.Draw(defs.Tilesets);
            _sectionGridPrefabs.Draw(defs.UnityGridLayers);
            
            LDtkEditorGUIUtility.DrawDivider();
        }

        private bool AssignJsonField()
        {
            SerializedProperty jsonProp = serializedObject.FindProperty(LDtkProjectImporter.JSON);
            
            if (_data != null)
            {
                return true;
            }
            
            Object jsonAsset = jsonProp.objectReferenceValue;
            if (jsonAsset == null)
            {
                Debug.LogError("Json asset is null, it's never expected to happen");
                return false;
            }
            
            LDtkProjectFile jsonFile = (LDtkProjectFile)jsonAsset;
            LdtkJson json = jsonFile.FromJson;
            if (json != null)
            {
                _data = jsonFile.FromJson;
                return true;
            }
            
            _data = null;
            Debug.LogError("LDtk: Invalid LDtk format");
            jsonProp.objectReferenceValue = null;
            return false;
        }

        /// <summary>
        /// Returns if this method had a problem.
        /// </summary>
        private bool LevelFieldsPrefabField(FieldDefinition[] defsEntityLayers)
        {
            if (defsEntityLayers == null || defsEntityLayers.Length == 0)
            {
                return false;
            }
            
            SerializedProperty levelFieldsProp = serializedObject.FindProperty(LDtkProjectImporter.LEVEL_FIELDS_PREFAB);
            
            
            Rect controlRect = EditorGUILayout.GetControlRect();
            
            if (levelFieldsProp.objectReferenceValue == null)
            {
                LDtkEditorGUI.DrawFieldWarning(controlRect, "The LDtk project has level fields defined, but there is no scripted level prefab assigned here.");
            }
            
            EditorGUI.PropertyField(controlRect, levelFieldsProp, LevelFields);
            return levelFieldsProp.objectReferenceValue == null;
        }

        private void DrawField(GUIContent content, string propName)
        {
            SerializedProperty pixelsPerUnitProp = serializedObject.FindProperty(propName);
            EditorGUILayout.PropertyField(pixelsPerUnitProp, content);
        }

        private void ApplyIfArraySizesChanged()
        {
            //IMPORTANT: if there are any new/removed array elements via this setup of automatically setting array size as LDtk definitions change,
            //then Unity's going to notice and make the apply/revert buttons appear active which gives us trouble when we try clicking out.
            //So, try applying right now when this specific case happens. Typically during the first OnInspectorGUI.
            
            if (_sectionDrawers.Any(drawer => drawer.HasResizedArrayPropThisUpdate))
            {
                Apply();
                Debug.Log("APPLIED");
            }
        }
        
        private void DrawPotentialProblem()
        {
            bool problem = _levelFieldsError || _sectionDrawers.Any(drawer => drawer.HasProblem);

            if (problem)
            {
                EditorGUILayout.HelpBox(
                    "LDtk Project asset configuration has unresolved issues, mouse over them to see the problem",
                    MessageType.Warning);
            }
        }
    }
}