﻿using System.IO;
using LDtkUnity.UnityAssets;
using UnityEditor;
using UnityEngine;

#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace LDtkUnity.Editor
{
    [HelpURL(LDtkHelpURL.JSON_LDTK_LEVEL)]
    [ScriptedImporter(1, EXTENSION)]
    public class LDtkLevelImporter : LDtkJsonImporter<LDtkLevelFile>
    {
        private const string EXTENSION = "ldtkl";

        protected override string Extension => EXTENSION;
    }
}