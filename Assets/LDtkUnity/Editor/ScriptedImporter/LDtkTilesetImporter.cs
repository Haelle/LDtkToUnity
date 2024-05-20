using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

#if LDTK_UNITY_ASEPRITE
using UnityEditor.U2D.Aseprite;
#endif

#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace LDtkUnity.Editor
{
    /// <summary>
    /// This importer is for generating everything that's related to a tileset definition.
    /// This is generated by the project importer.
    /// This has no dependency back to the project importer, only the texture it references.
    /// </summary>
    [HelpURL(LDtkHelpURL.IMPORTER_LDTK_TILESET)]
    [ScriptedImporter(LDtkImporterConsts.TILESET_VERSION, LDtkImporterConsts.TILESET_EXT, LDtkImporterConsts.TILESET_ORDER)]
    internal sealed partial class LDtkTilesetImporter : LDtkJsonImporter<LDtkTilesetFile>
    {
        public const string PIXELS_PER_UNIT = nameof(_pixelsPerUnit);
        
        [SerializeField] internal int _pixelsPerUnit = -1;
        /// <summary>
        /// Holds onto all the standard grid-sized tiles. This serializes the sprite's changed settings between reimports, like pivot or physics shape.
        /// </summary>
        [SerializeField] internal List<LDtkSpriteRect> _sprites = new List<LDtkSpriteRect>();
        
        /// <summary>
        /// Any sprites that were defined from entity/level fields.
        /// It's separate because we don't want to draw them in the sprite editor window, or otherwise make them configurable.
        /// Also, because they won't have tilemap assets generated for them anyway, as their size wouldn't fit in the tilemap.
        /// </summary>
        private List<LDtkSpriteRect> _additionalTiles = new List<LDtkSpriteRect>();
        
        [SerializeField] internal SecondarySpriteTexture[] _secondaryTextures;
    
        private Texture2D _cachedExternalTex;
        private Texture2D _cachedTex;
        private LDtkArtifactAssetsTileset _cachedArtifacts;

        /// <summary>
        /// filled by deserializing
        /// </summary>
        private LDtkTilesetDefinitionWrapper _definition;
        private TilesetDefinition _json;
        
#if LDTK_UNITY_ASEPRITE
        private AsepriteImporter _srcAsepriteImporter;
#endif
        private TextureImporter _srcTextureImporter;
        private LDtkTilesetFile _tilesetFile;
        private string _texturePath;
        
        
        public static string[] _previousDependencies;
        protected override string[] GetGatheredDependencies() => _previousDependencies;


        //this will run upon standard reset, but also upon the meta file generation during the first import
        private void Reset()
        {
            LDtkPpuInitializer ppu = new LDtkPpuInitializer(_pixelsPerUnit, GetProjectPath(), assetPath);
            if (ppu.OnResetImporter())
            {
                _pixelsPerUnit = ppu.PixelsPerUnit;
                EditorUtility.SetDirty(this);
                SaveAndReimport();
            }
        }

        private static string[] GatherDependenciesFromSourceFile(string path)
        {
            if (LDtkPrefs.VerboseLogging)
            {
                LDtkDebug.Log($"GatherDependenciesFromSourceFile Tileset {path}");
            }

            //this depends on the texture
            //todo add a digger for getting the RelPath
            LDtkProfiler.BeginWriting($"GatherDependenciesFromSourceFile/{Path.GetFileName(path)}");
            string texPath = PathToTexture(path);
            texPath = !File.Exists(texPath) ? string.Empty : texPath;
            _previousDependencies = string.IsNullOrEmpty(texPath) ? Array.Empty<string>() : new []{texPath};
            LDtkProfiler.EndWriting();
            
            return _previousDependencies;
        }

        protected override void Import()
        {
            LDtkProfiler.BeginSample("DeserializeAndAssign");
            if (!DeserializeAndAssign())
            {
                LDtkProfiler.EndSample();
                FailImport();
                return;
            }
            LDtkProfiler.EndSample();

            //it's possible to select an empty tileset in LDtk
            if (IsNullTileset())
            {
                ImportContext.AddObjectToAsset("texture", new Texture2D(0,0), LDtkIconUtility.LoadTilesetFileIcon());
                return;
            }
            
            LDtkProfiler.BeginSample("GetTextureImporterPlatformSettings");
            TextureImporterPlatformSettings platformSettings = GetTextureImporterPlatformSettings();
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("CorrectTheTexture");
            //we're not auto-changing the textures because trying to make the changes via multi-selection doesnt work well. Could do some auto fixup for the texture maybe?
            if (HasTextureIssue(platformSettings))
            {
                LDtkProfiler.EndSample();
                FailImport();
                return;
            }
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("SetPixelsPerUnit");
            LDtkPpuInitializer ppu = new LDtkPpuInitializer(_pixelsPerUnit, GetProjectPath(), assetPath);
            if (ppu.OnResetImporter())
            {
                _pixelsPerUnit = ppu.PixelsPerUnit;
                EditorUtility.SetDirty(this);
            }
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("GetStandardSpriteRectsForDefinition");
            var rects = ReadSourceRectsFromJsonDefinition(_definition.Def);
            LDtkProfiler.EndSample();

            LDtkProfiler.BeginSample("ReformatRectMetaData");
            if (ReformatRectMetaData(rects))
            {
                EditorUtility.SetDirty(this);
            }
            LDtkProfiler.EndSample();

            LDtkProfiler.BeginSample("ReformatAdditionalTiles");
            ReformatAdditionalTiles();
            LDtkProfiler.EndSample();

            LDtkProfiler.BeginSample("PrepareGenerate");
            if (!PrepareGenerate(platformSettings, out TextureGenerationOutput output))
            {
                FailImport();
                LDtkProfiler.EndSample();
                return;
            }
            LDtkProfiler.EndSample();

            Texture2D outputTexture = output.texture;
            if (outputTexture == null)
            {
                Logger.LogError("No Texture was generated. Possibly because it failed to generate texture.");
                FailImport();
                return;
            }
            if (output.sprites.IsNullOrEmpty())
            {
                Logger.LogError("No Sprites are generated. ");
                FailImport();
                return;
            }
            if (!string.IsNullOrEmpty(output.importInspectorWarnings))
            {
                Logger.LogWarning(output.importInspectorWarnings);
            }
            if (output.importWarnings != null)
            {
                foreach (var warning in output.importWarnings)
                {
                    Logger.LogWarning(warning);
                }
            }
            if (output.thumbNail == null)
            {
                Logger.LogWarning("Thumbnail generation fail");
            }
            
            outputTexture.name = AssetName;
            
            LDtkProfiler.BeginSample("MakeAndCacheArtifacts");
            LDtkArtifactAssetsTileset artifacts = MakeAndCacheArtifacts(output);
            LDtkProfiler.EndSample();

            ImportContext.AddObjectToAsset("artifactCache", artifacts, (Texture2D)LDtkIconUtility.GetUnityIcon("Tilemap"));
            ImportContext.AddObjectToAsset("texture", outputTexture, LDtkIconUtility.LoadTilesetFileIcon());
            ImportContext.AddObjectToAsset("tilesetFile", _tilesetFile, LDtkIconUtility.LoadTilesetIcon());
            
            ImportContext.SetMainObject(outputTexture);

            LDtkTilemapColliderReset.TilemapColliderTileUpdate();
        }

        private LDtkArtifactAssetsTileset MakeAndCacheArtifacts(TextureGenerationOutput output)
        {
            LDtkArtifactAssetsTileset artifacts = ScriptableObject.CreateInstance<LDtkArtifactAssetsTileset>();
            artifacts.name = $"_{_definition.Def.Identifier}_Artifacts";
            
            LDtkProfiler.BeginSample("InitArrays");
            artifacts._sprites = new List<Sprite>(_sprites.Count);
            artifacts._tiles = new List<LDtkTilesetTile>(_sprites.Count);
            artifacts._additionalSprites = new List<Sprite>(_additionalTiles.Count);
            LDtkProfiler.EndSample();

            var customData = _definition.Def.CustomDataToDictionary();
            var enumTags = _definition.Def.EnumTagsToDictionary();
            
            for (int i = 0; i < output.sprites.Length; i++)
            {
                LDtkProfiler.BeginSample("AddTile");
                Sprite spr = output.sprites[i];
                spr.hideFlags = HideFlags.HideInHierarchy;
                ImportContext.AddObjectToAsset(spr.name, spr);
                LDtkProfiler.EndSample();

                //any indexes past the sprite count is additional sprites. Don't make tile, just sprite.
                if (i >= _sprites.Count)
                {
                    LDtkProfiler.BeginSample("AddAdditionalSprite");
                    artifacts._additionalSprites.Add(spr);
                    LDtkProfiler.EndSample();
                    continue;
                }
                
                LDtkProfiler.BeginSample("AddOffsetToPhysicsShape");
                AddOffsetToPhysicsShape(spr, i);
                LDtkProfiler.EndSample();

                LDtkProfiler.BeginSample("CreateLDtkTilesetTile");
                LDtkTilesetTile newTilesetTile = ScriptableObject.CreateInstance<LDtkTilesetTile>();
                newTilesetTile.name = spr.name;
                newTilesetTile._sprite = spr;
                newTilesetTile._type = GetColliderTypeForSprite(spr);
                newTilesetTile._tileId = i;
                newTilesetTile.hideFlags = HideFlags.HideInHierarchy;
                if (customData.TryGetValue(i, out string cd))
                {
                    newTilesetTile._customData = cd;
                }
                if (enumTags.TryGetValue(i, out List<string> et))
                {
                    newTilesetTile._enumTagValues = et;
                }
                LDtkProfiler.EndSample();
                
                LDtkProfiler.BeginSample("AddTile");
                ImportContext.AddObjectToAsset(newTilesetTile.name, newTilesetTile);
                artifacts._sprites.Add(spr);
                artifacts._tiles.Add(newTilesetTile);
                LDtkProfiler.EndSample();
            }

            LDtkProfiler.BeginSample("TryParseCustomData");
            //process these after all the tiles are created because we might reference other tiles for animation
            foreach (var tile in artifacts._tiles)
            {
                TryParseCustomData(artifacts, tile);
            }
            LDtkProfiler.EndSample();

            return artifacts;
        }
        
        private void TryParseCustomData(LDtkArtifactAssetsTileset artifacts, LDtkTilesetTile tile)
        {
            string customData = tile._customData;
            if (customData.IsNullOrEmpty())
            {
                return;
            }
            
            string[] lines = customData.Split('\n');
            foreach (string line in lines)
            {
                //animatedSprites
                {
                    if (ParseAndGetTokensAsInt(tile, line, "animatedSprites", out int[] spriteIdTokens))
                    {
                        tile._animatedSprites = new Sprite[spriteIdTokens.Length];
                        for (int i = 0; i < spriteIdTokens.Length; i++)
                        {
                            int spriteId = spriteIdTokens[i];

                            if (spriteId < 0 || spriteId >= artifacts._sprites.Count)
                            {
                                Logger.LogWarning($"Issue parsing animatedSprites for tile \"{tile.name}\". Tile ID {spriteId} is out of range");
                                continue;
                            }
                            
                            tile._animatedSprites[i] = artifacts._sprites[spriteId];
                        }
                        continue;
                    }
                }

                //animationSpeed
                {
                    if (ParseAndGetTokensAsFloat(tile, line, "animationSpeed", out float[] speedTokens))
                    {
                        if (speedTokens.Length == 1)
                        {
                            tile._animationSpeedMin = speedTokens[0];
                            tile._animationSpeedMax = speedTokens[0];
                            continue;
                        }
                    
                        if (speedTokens.Length == 2)
                        {
                            tile._animationSpeedMin = speedTokens[0];
                            tile._animationSpeedMax = speedTokens[1];
                            continue;
                        }
                    
                        Logger.LogWarning($"Issue parsing animationSpeed for tile \"{tile.name}\". Expected 1 or 2 decimal numbers but there were {speedTokens.Length}");
                        continue;
                    }
                }

                //animationStartTime
                {
                    if (ParseAndGetTokensAsFloat(tile, line, "animationStartTime", out float[] startTimeTokens))
                    {
                        if (startTimeTokens.Length == 1)
                        {
                            tile._animationStartTimeMin = startTimeTokens[0];
                            tile._animationStartTimeMax = startTimeTokens[0];
                            continue;
                        }
                    
                        if (startTimeTokens.Length == 2)
                        {
                            tile._animationStartTimeMin = startTimeTokens[0];
                            tile._animationStartTimeMax = startTimeTokens[1];
                            continue;
                        }
                    
                        Logger.LogWarning($"Issue parsing animationStartTime for tile \"{tile.name}\". Expected 1 or 2 decimal numbers but there were {startTimeTokens.Length}");
                        continue;
                    }
                }

                //animationStartFrame
                {
                    if (ParseAndGetTokensAsInt(tile, line, "animationStartFrame", out int[] startFrameTokens))
                    {
                        if (startFrameTokens.Length == 1)
                        {
                            tile._animationStartFrameMin = startFrameTokens[0];
                            tile._animationStartFrameMax = startFrameTokens[0];
                            continue;
                        }
                    
                        if (startFrameTokens.Length == 2)
                        {
                            tile._animationStartFrameMin = startFrameTokens[0];
                            tile._animationStartFrameMax = startFrameTokens[1];
                            continue;
                        }
                    
                        Logger.LogWarning($"Issue parsing animationStartFrame for tile \"{tile.name}\". Expected 1 or 2 ints but there were {startFrameTokens.Length}");
                        continue;
                    }
                }
            }
        }

        private bool ParseAndGetTokensAsInt(LDtkTilesetTile tile, string line, string keyword, out int[] tokens)
        {
            if (!ParseAndGetTokensAsString(line, keyword, out string[] tokenStrings))
            {
                tokens = null;
                return false;
            }
            
            tokens = new int[tokenStrings.Length];
            for (int i = 0; i < tokenStrings.Length; i++)
            {
                string tokenString = tokenStrings[i];
                if (int.TryParse(tokenString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVaue))
                {
                    tokens[i] = intVaue;
                    continue;
                }
                Logger.LogWarning($"Issue parsing \"{keyword}\"'s token {i} for tile \"{tile.name}\". Couldn't parse into int: \"{tokenString}\"");
            }
            return true;
        }

        private bool ParseAndGetTokensAsFloat(LDtkTilesetTile tile, string line, string keyword, out float[] tokens)
        {
            if (!ParseAndGetTokensAsString(line, keyword, out string[] tokenStrings))
            {
                tokens = null;
                return false;
            }
            
            tokens = new float[tokenStrings.Length];
            for (int i = 0; i < tokenStrings.Length; i++)
            {
                string tokenString = tokenStrings[i];
                if (float.TryParse(tokenString, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                {
                    tokens[i] = floatValue;
                    continue;
                }
                Logger.LogWarning($"Issue parsing \"{keyword}\"'s token for tile \"{tile.name}\". Couldn't parse into float: \"{tokenString}\"");
            }
            return true;
        }

        private bool ParseAndGetTokensAsString(string line, string keyword, out string[] tokens)
        {
            if (!line.StartsWith(keyword))
            {
                tokens = null;
                return false;
            }
            
            string strippedOfKeyword = line.Replace(keyword, "");
            tokens = strippedOfKeyword.Split(new []{','}, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                tokens[i] = tokens[i].Trim();
            }

            return true;
        }
        
        Tile.ColliderType GetColliderTypeForSprite(Sprite spr)
        {
            int shapeCount = spr.GetPhysicsShapeCount();
            if (shapeCount == 0)
            {
                return Tile.ColliderType.None;
            }
            if (shapeCount == 1)
            {
                List<Vector2> list = new List<Vector2>();
                spr.GetPhysicsShape(0, list);
                if (IsShapeSetForGrid(list))
                {
                    return Tile.ColliderType.Grid;
                }
            }
            return Tile.ColliderType.Sprite;
        }
        private static Vector2 GridCheck1 = new Vector2(-0.5f, -0.5f);
        private static Vector2 GridCheck2 = new Vector2(-0.5f, 0.5f);
        private static Vector2 GridCheck3 = new Vector2(0.5f, 0.5f);
        private static Vector2 GridCheck4 = new Vector2(0.5f, -0.5f);
        public static bool IsShapeSetForGrid(List<Vector2> shape)
        {
            return shape.Count == 4 &&
                   shape.Any(p => p == GridCheck1) &&
                   shape.Any(p => p == GridCheck2) &&
                   shape.Any(p => p == GridCheck3) &&
                   shape.Any(p => p == GridCheck4);
        }

        private void ReformatAdditionalTiles()
        {
            Debug.Assert(_definition != null);
            //Debug.Assert();
            
            var additionalRects = _definition.Rects;
            if (additionalRects.IsNullOrEmpty())
            {
                return;
            }

            _additionalTiles.Clear();
            for (int i = _additionalTiles.Count; i < additionalRects.Count; i++)
            {
                var rect = _definition.Rects[i].ToRect();
                rect = LDtkCoordConverter.ImageSlice(rect, _definition.Def.PxHei);
                LDtkSpriteRect newRect = new LDtkSpriteRect
                {
                    border = Vector4.zero,
                    pivot = new Vector2(0.5f, 0.5f),
                    alignment = SpriteAlignment.Center,
                    rect = rect,
                    spriteID = GUID.Generate(),
                    name = MakeAssetName()
                };
                _additionalTiles.Add(newRect);
                
                string MakeAssetName()
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append(_definition.Def.Identifier);
                    sb.Append('_');
                    sb.Append(rect.x);
                    sb.Append('_');
                    sb.Append(rect.y);
                    sb.Append('_');
                    sb.Append(rect.width);
                    sb.Append('_');
                    sb.Append(rect.height);
                    return sb.ToString();
                }
            }
            
            Debug.Assert(_additionalTiles.Count == additionalRects.Count);
        }

        private bool PrepareGenerate(TextureImporterPlatformSettings platformSettings, out TextureGenerationOutput output)
        {
            Debug.Assert(_pixelsPerUnit > 0, $"_pixelsPerUnit was {_pixelsPerUnit}");
            
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            
#if LDTK_UNITY_ASEPRITE
            if (_srcAsepriteImporter)
            {
                _srcAsepriteImporter.ReadTextureSettings(importerSettings);
            }
            else
#endif
            {
                _srcTextureImporter.ReadTextureSettings(importerSettings);
            }

            if (importerSettings.textureType != TextureImporterType.Sprite)
            {
                output = default;
                Logger.LogError($"Didn't generate the texture and sprites for \"{AssetName}\" because the source texture's TextureType is not \"Sprite\".");
                return false;
            }
            
            platformSettings.format = TextureImporterFormat.RGBA32;
            importerSettings.spritePixelsPerUnit = _pixelsPerUnit;
            importerSettings.filterMode = FilterMode.Point;

            Texture2D copy;
            
#if LDTK_UNITY_ASEPRITE
            if (_srcAsepriteImporter)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PathToTexture(assetPath));
                if (sprite == null)
                {
                    output = default;
                    Logger.LogError($"Failed to load the aseprite sprite for \"{AssetName}\". Either the Aseprite file failed to import, or the aseprite file's import settings are configured to not generate a sprite.");
                    return false;
                }
                
                LDtkProfiler.BeginSample("GenerateAsepriteTexture");
                copy = GenerateTextureFromAseprite(sprite);
                LDtkProfiler.EndSample();
            }
            else
#endif
            {
                LDtkProfiler.BeginSample("LoadExternalTex");
                Texture2D tex = LoadExternalTex();
                LDtkProfiler.EndSample();
                
                LDtkProfiler.BeginSample("CopyTexture");
                copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
                Graphics.CopyTexture(tex, copy);
                LDtkProfiler.EndSample();
            }

            LDtkProfiler.BeginSample("GetRawTextureData");
            NativeArray<Color32> rawData = copy.GetRawTextureData<Color32>();
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("TextureGeneration.Generate");
            output = TextureGeneration.Generate(
                ImportContext, rawData, copy.width, copy.height, _sprites.Concat(_additionalTiles).ToArray(),
                platformSettings, importerSettings, string.Empty, _secondaryTextures);
            LDtkProfiler.EndSample();
            
            return true;
        }

        private Texture2D GenerateTextureFromAseprite(Sprite sprite)
        {
            Texture2D croppedTexture = new Texture2D(_json.PxWid, _json.PxHei, TextureFormat.RGBA32, false, false);

            Color32[] colors = new Color32[_json.PxWid * _json.PxHei];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(0, 0, 0, 0);
            }
            croppedTexture.SetPixels32(colors);

            Color[] pixels = sprite.texture.GetPixels((int)sprite.textureRect.x, 
                (int)sprite.textureRect.y, 
                (int)sprite.textureRect.width, 
                (int)sprite.textureRect.height);
            croppedTexture.SetPixels(0 ,_json.PxHei - (int)sprite.rect.height, (int)sprite.rect.width, (int)sprite.rect.height, pixels);
            
            croppedTexture.Apply(false, false);

            return croppedTexture;
        }
        

        private TextureImporterPlatformSettings GetTextureImporterPlatformSettings()
        {
#if LDTK_UNITY_ASEPRITE
            if (_srcAsepriteImporter)
            {
                return _srcAsepriteImporter.GetImporterPlatformSettings(EditorUserBuildSettings.activeBuildTarget);
            }
#endif
            
            string platform = EditorUserBuildSettings.activeBuildTarget.ToString();
            TextureImporterPlatformSettings platformSettings = _srcTextureImporter.GetPlatformTextureSettings(platform);
            return platformSettings.overridden ? platformSettings : _srcTextureImporter.GetDefaultPlatformTextureSettings();
        }

        private bool DeserializeAndAssign()
        {
            //deserialize first. required for the path to the texture importer 
            try
            {
                _definition = FromJson<LDtkTilesetDefinitionWrapper>();
                _json = _definition.Def;
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString());
                return false;
            }
            
            if (IsNullTileset())
            {
                return true;
            }
            
            LDtkProfiler.BeginSample("CacheTextureImporterOrAsepriteImporter");
            if (!CacheTextureImporterOrAsepriteImporter())
            {
                LDtkProfiler.EndSample();
                return false;
            }
            LDtkProfiler.EndSample();

            LDtkProfiler.BeginSample("AddTilesetSubAsset");
            _tilesetFile = ReadAssetText();
            _tilesetFile.name = _tilesetFile.name.Insert(0, "_");
            LDtkProfiler.EndSample();
            
            if (_tilesetFile == null)
            {
                Logger.LogError("Tried to build tileset, but the tileset json ScriptableObject was null");
                return false;
            }
            
            return true;
        }

        private bool IsNullTileset()
        {
            return !_json.IsEmbedAtlas && _json.RelPath == null;
        }
        
        private bool CacheTextureImporterOrAsepriteImporter()
        {
            string path = PathToTexture(assetPath, _json);

            //First check embed atlas
            if (_json.IsEmbedAtlas && path.IsNullOrEmpty())
            {
                Logger.LogError($"Tried to build the internal icons \"{AssetName}\", But the internal icons was not assigned in Unity's project settings. " +
                                $"You can add the texture by going to Edit > Project Settings > LDtk To Unity");
                return false;
            }
            
            if (!_json.IsEmbedAtlas && _json.RelPath.Length == 0)
            {
                Logger.LogError($"The tileset relative path was empty! Try fixing the Tileset path in the LDtk editor for \"{assetPath}\"");
                return false;
            }
            

            //Then check aseprite
            if (LDtkRelativeGetterTilesetTexture.IsAsepriteAsset(path))
            {
#if LDTK_UNITY_ASEPRITE
                _srcAsepriteImporter = (AsepriteImporter)GetAtPath(path);
                if (_srcAsepriteImporter == null)
                {
                    Logger.LogError($"Tried to build tileset {AssetName}, but the aseprite importer was not found at \"{path}\". Is this tileset asset in a folder relative to the LDtk project file? Ensure that it's relativity is maintained if the project was moved also.");
                    return false;
                }
#else
                string fileName = Path.GetFileName(path);
                Logger.LogError($"Tried loading an aseprite file \"{fileName}\", but the aseprite importer is not installed or below version 1.0.0. Add: com.unity.2d.aseprite. Requires Unity 2021.3.15 or newer");
                return false;
#endif
            }
            else
            {
                _srcTextureImporter = (TextureImporter)GetAtPath(path);
                if (_srcTextureImporter == null)
                {
                    Logger.LogError($"Tried to build tileset {AssetName}, but the texture importer was not found at \"{path}\". Is this tileset asset in a folder relative to the LDtk project file? Ensure that it's relativity is maintained if the project was moved also.");
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Only use when needed, it performs a deserialize. look at optimizing if it's expensive
        /// </summary>
        private static string PathToTexture(string assetPath, TilesetDefinition def = null)
        {
            if (def == null)
            {
                def = FromJson<LDtkTilesetDefinitionWrapper>(assetPath).Def;
            }
            
            if (def.IsEmbedAtlas)
            {
                string iconsPath = LDtkProjectSettings.InternalIconsTexturePath;
                return iconsPath.IsNullOrEmpty() ? string.Empty : iconsPath;
            }

            LDtkRelativeGetterTilesetTexture getter = new LDtkRelativeGetterTilesetTexture();
            string pathFrom = Path.Combine(assetPath, "..");
            pathFrom = LDtkPathUtility.CleanPath(pathFrom);
            string path = getter.GetPath(def, pathFrom);
            //Debug.Log($"relative from {pathFrom}. path of texture importer was {path}");
            return path;
        }

        //todo really look at this function and understand if it's truly nessesary.
        //experimnent with using it on and off and checking how builds behave as a result.
        //will they log like this: https://forum.unity.com/threads/sprite-outline-generation-failed-could-not-read-texture-pixel-data-when-building-the-game.861775/
        
        private void AddOffsetToPhysicsShape(Sprite spr, int i)
        {
            LDtkProfiler.BeginSample("GetSpriteData");
            LDtkSpriteRect spriteData = _sprites[i];
            //LDtkSpriteRect spriteData = GetSpriteData(spr.name);
            LDtkProfiler.EndSample();

            LDtkProfiler.BeginSample("GetOutlines");
            List<Vector2[]> srcShapes = spriteData.GetOutlines();
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("MakeNewShapes");
            List<Vector2[]> newShapes = new List<Vector2[]>();
            foreach (Vector2[] srcOutline in srcShapes)
            {
                Vector2[] newOutline = new Vector2[srcOutline.Length];
                for (int ii = 0; ii < srcOutline.Length; ii++)
                {
                    Vector2 point = srcOutline[ii];
                    point += spr.rect.size * 0.5f;
                    newOutline[ii] = point;
                }
                newShapes.Add(newOutline);
            }
            LDtkProfiler.EndSample();
            
            LDtkProfiler.BeginSample("OverridePhysicsShape");
            spr.OverridePhysicsShape(newShapes);
            LDtkProfiler.EndSample();
        }

        private void ForceUpdateSpriteDataName(SpriteRect spr)
        {
            spr.name = $"{AssetName}_{spr.rect.x}_{spr.rect.y}_{spr.rect.width}_{spr.rect.height}";
        }

        private static readonly int[] MaxSizes = new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
        private bool HasTextureIssue(TextureImporterPlatformSettings platformSettings)
        {
#if LDTK_UNITY_ASEPRITE
            AssetImporter importer = _srcAsepriteImporter != null ? (AssetImporter)_srcAsepriteImporter : _srcTextureImporter; 
#else
            AssetImporter importer = _srcTextureImporter;
#endif
            
            bool issue = false;

            // need proper resolution
            if (platformSettings.maxTextureSize < _json.PxWid || platformSettings.maxTextureSize < _json.PxHei)
            {
                int highest = Mathf.Max(_json.PxWid, _json.PxHei);

                int resolution = 16384;
                for (int i = 0; i < MaxSizes.Length; i++)
                {
                    int size = MaxSizes[i];
                    if (highest <= size)
                    {
                        resolution = size;
                        break;
                    }
                }

                issue = true;
                Logger.LogError($"The texture at \"{importer.assetPath}\" maxTextureSize needs to at least be {resolution}.\n(From {assetPath})", importer);
                //platformSettings.maxTextureSize = resolution;
            }

            //this is required or else the texture generator does not comply
            if (platformSettings.format != TextureImporterFormat.RGBA32)
            {
                issue = true;
                //platformSettings.format = TextureImporterFormat.RGBA32;
                Logger.LogError($"The texture at \"{importer.assetPath}\" needs to have a compression format of {TextureImporterFormat.RGBA32}\n(From {assetPath})", importer);
            }

            //need to read the texture to make our own texture generation result
            /*if (!textureImporter.isReadable)
            {
                issue = true;
                //textureImporter.isReadable = true;
                Logger.LogError($"The texture \"{textureImporter.assetPath}\" was not readable. Change it.", this);
            }*/

            
            return issue;
        }

        private Texture2D LoadExternalTex(bool forceLoad = false)
        {
            //this is important: in case the importer was destroyed via file delete
            if (this == null)
            {
                return null;
            }
            
            if (_cachedExternalTex == null || forceLoad)
            {
                _cachedExternalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PathToTexture(assetPath));
            }
            return _cachedExternalTex;
        }
        private Texture2D LoadTex(bool forceLoad = false)
        {
            //this is important: in case the importer was destroyed via file delete
            if (this == null)
            {
                return null;
            }
            
            if (_cachedTex == null || forceLoad)
            {
                _cachedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            return _cachedTex;
        }
        
        private LDtkSpriteRect GetSpriteData(GUID guid)
        {
            LDtkSpriteRect data = _sprites.FirstOrDefault(x => x.spriteID == guid);
            Debug.Assert(data != null, $"Sprite data not found for GUID: {guid.ToString()}");
            return data;
        }

        private LDtkSpriteRect GetSpriteData(string spriteName)
        {
            LDtkSpriteRect data = _sprites.FirstOrDefault(x => x.name == spriteName);
            Debug.Assert(data != null, $"Sprite data not found for name: {spriteName}");
            return data;
        }
        
        public LDtkArtifactAssetsTileset LoadArtifacts(LDtkDebugInstance projectCtx)
        {
            if (_cachedArtifacts)
            {
                return _cachedArtifacts;
            }
            
            LDtkProfiler.BeginSample($"LoadMainAssetAtPath<LDtkArtifactAssetsTileset> {AssetName}");
            _cachedArtifacts = AssetDatabase.LoadAssetAtPath<LDtkArtifactAssetsTileset>(assetPath);
            LDtkProfiler.EndSample();
            
            //It's possible that the artifact assets don't exist, either because the texture importer failed to import, or the artifact assets weren't produced due to being an aseprite file or otherwise
            if (_cachedArtifacts == null)
            {
                LDtkDebug.LogError($"Loading artifacts didn't work for getting tileset sprite artifacts. You should investigate the tileset file at \"{assetPath}\"", projectCtx);
                return null;
            }
            
            return _cachedArtifacts;
        }
        
    }
}