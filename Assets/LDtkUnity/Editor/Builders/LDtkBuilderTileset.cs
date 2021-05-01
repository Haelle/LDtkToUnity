﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Tile = UnityEngine.Tilemaps.Tile;

namespace LDtkUnity.Editor.Builders
{
    public class LDtkBuilderTileset : LDtkLayerBuilder
    {
        
        
        public LDtkBuilderTileset(LayerInstance layer, LDtkProjectImporter importer) : base(layer, importer)
        {
        }

        public void BuildTileset(TileInstance[] tiles, Tilemap[] tilemaps)
        {
            
            TilesetDefinition definition = Layer.IsAutoLayer
                ? Layer.Definition.AutoTilesetDefinition
                : Layer.Definition.TilesetDefinition;

            LDtkRelativeGetterTilesetTexture getter = new LDtkRelativeGetterTilesetTexture();
            Texture2D texAsset = getter.GetRelativeAsset(definition, Importer.assetPath);
            if (texAsset == null)
            {
                return;
            }
            
            //it's important to allow the sprite to have read/write enabled
            if (!texAsset.isReadable)
            {
                Debug.LogError($"Tileset \"{texAsset.name}\" texture does not have Read/Write Enabled, is it enabled?", texAsset);
                return;
            }
            
            //figure out if we have already built a tile in this position. otherwise, build up to the next tilemap
            Dictionary<Vector2Int, int> builtTileLayering = new Dictionary<Vector2Int, int>();
            
            foreach (TileInstance tileData in tiles)
            {
                Vector2Int px = tileData.UnityPx;
                int tilemapLayer = GetTilemapLayerToBuildOn(builtTileLayering, px, tilemaps.Length-1);

                Tilemap tilemap = tilemaps[tilemapLayer];
                GetTile(tileData, texAsset, tilemap);
            }

            foreach (Tilemap tilemap in tilemaps)
            {
                LDtkEditorUtil.Dirty(tilemap);
            }
        }

        private void GetTile(TileInstance tileData, Texture2D texAsset, Tilemap tilemap)
        {
            Vector2Int imageSliceCoord = LDtkToolOriginCoordConverter.ImageSliceCoord(tileData.UnitySrc, texAsset.height, Importer.PixelsPerUnit);
            LDtkArtifactAssets artifactAssets = null;//Importer.GetTileCollection(Layer.TilesetDefinition.Identifier); todo

            if (artifactAssets == null)
            {
                return;
            }
            
            //make this the file name, but not a means of geting the file
            string key = LDtkKeyFormatUtil.TilesetKeyFormat(texAsset, imageSliceCoord);
            
            //TODO Create the sprite within this scope and then cache it to that cool asset list. we create the assets on site instead of beforehand. and try to add the named tile if it doesn't exist before
            LDtkArtifactAssets assets; //this artifact assets would be provided from a global scope like the LDtkProjectBuilder

            Sprite GetSprite()
            {
                Sprite sprite = assets.GetSpriteByName(key);
                if (sprite != null)
                {
                    return sprite;
                }
                
                sprite = null; //todo CREATE the sprite onsite
                assets.TryAddSprite(sprite);
                return sprite;
            }
            
            
            //try gettin
            
            TileBase tile = artifactAssets.GetTileByName(key);
            
            if (tile == null)
            {
                return;
            }
            
            Vector2Int coord = GetConvertedCoord(tileData);
            Vector3Int tilemapCoord = new Vector3Int(coord.x, coord.y, 0);
            tilemap.SetTile(tilemapCoord, tile);
            SetTileFlips(tilemap, tileData, coord); 
        }

        private Vector2Int GetConvertedCoord(TileInstance tileData)
        {
            Vector2Int coord = new Vector2Int(tileData.UnityPx.x / (int) Layer.GridSize,
                tileData.UnityPx.y / (int) Layer.GridSize);
            coord = LDtkToolOriginCoordConverter.ConvertCell(coord, (int) Layer.CHei);
            return coord;
        }

        private int GetTilemapLayerToBuildOn(Dictionary<Vector2Int, int> builtTileLayering, Vector2Int key, int startingNumber)
        {
            if (builtTileLayering.ContainsKey(key))
            {
                return --builtTileLayering[key];
            }
            
            builtTileLayering.Add(key, startingNumber);
            return startingNumber;
        }
        
        private void SetTileFlips(Tilemap tilemap, TileInstance tileData, Vector2Int coord)
        {
            float rotY = tileData.FlipX ? 180 : 0;
            float rotX = tileData.FlipY ? 180 : 0;
            Quaternion rot = Quaternion.Euler(rotX, rotY, 0);
            Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);
            tilemap.SetTransformMatrix((Vector3Int) coord, matrix);
        }
    }
}
