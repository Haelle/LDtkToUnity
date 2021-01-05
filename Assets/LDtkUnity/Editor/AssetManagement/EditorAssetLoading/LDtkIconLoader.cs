﻿using UnityEngine;

namespace LDtkUnity.Editor
{
    public static class LDtkIconLoader
    {
        private const string ROOT = "Icons/";
        
        private const string PROJECT = "LDtkProjectIcon.png";
        private const string SIMPLE = "LDtkSimpleIcon.png";
        private const string ENTITY = "EntityIcon.png";
        private const string TILESET = "TilesetIcon.png";
        private const string AUTO_LAYER = "AutoLayerIcon.png";
        private const string FILE = "FileIcon.png";
        private const string ENUM = "EnumIcon.png";
        private const string WORLD = "WorldIcon.png";
        private const string INT_GRID = "IntGridIcon.png";

        private static Texture2D _cachedProjectIcon;
        private static Texture2D _cachedSimpleIcon;
        private static Texture2D _cachedEntityIcon;
        private static Texture2D _cachedTilesetIcon;
        private static Texture2D _cachedAutoLayerIcon;
        private static Texture2D _cachedFileIcon;
        private static Texture2D _cachedEnumIcon;
        private static Texture2D _cachedWorldIcon;
        private static Texture2D _cachedIntGridIcon;
        
        public static Texture2D LoadProjectIcon() => LoadIcon(PROJECT, _cachedProjectIcon);
        public static Texture2D LoadEntityIcon() => LoadIcon(ENTITY, _cachedEntityIcon);
        public static Texture2D LoadSimpleIcon() => LoadIcon(SIMPLE, _cachedSimpleIcon);
        public static Texture2D LoadTilesetIcon() => LoadIcon(TILESET, _cachedTilesetIcon);
        public static Texture2D LoadAutoLayerIcon() => LoadIcon(AUTO_LAYER, _cachedAutoLayerIcon);
        public static Texture2D LoadFileIcon() => LoadIcon(FILE, _cachedFileIcon);
        public static Texture2D LoadEnumIcon() => LoadIcon(ENUM, _cachedEnumIcon);
        public static Texture2D LoadWorldIcon() => LoadIcon(WORLD, _cachedWorldIcon);
        public static Texture2D LoadIntGridIcon() => LoadIcon(INT_GRID, _cachedIntGridIcon);

        private static Texture2D LoadIcon(string path, Texture2D cached)
        {
            if (cached != null)
            {
                return cached;
            }
            return LDtkInternalLoader.Load<Texture2D>(ROOT + path);
        }

        //TODO eventually get this ran to prevent too much memory being used (though the textures are pretty small anyways)
        public static void Dispose()
        {
            _cachedProjectIcon = null;
            _cachedSimpleIcon = null;
            _cachedEntityIcon = null;
            _cachedTilesetIcon = null;
            _cachedAutoLayerIcon = null;
            _cachedFileIcon = null;
            _cachedEnumIcon = null;
            _cachedWorldIcon = null;
            _cachedIntGridIcon = null;
        }
    }
}