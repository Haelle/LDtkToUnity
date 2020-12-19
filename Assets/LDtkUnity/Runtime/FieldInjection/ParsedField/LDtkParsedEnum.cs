﻿using System;
using UnityEngine;

namespace LDtkUnity.FieldInjection
{
    public sealed class LDtkParsedEnum : ILDtkValueParser
    {
        private Type _enumType;
        
        public bool IsType(Type triedType) => triedType.IsEnum;
        
        public void SetEnumType(Type type)
        {
            if (!type.IsEnum)
            {
                Debug.LogError("Trying to set a non-enum as it's stored type in LDtkParsedEnum", LDtkInjectionErrorContext.Context);
            }
            _enumType = type;
        }
        
        public object ParseValue(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                Debug.LogWarning($"LDtk: Input enum was an empty string; Setting as default enum value. Undefined in LDtk editor?", LDtkInjectionErrorContext.Context);
                return default;
            }

            //give enum value an underscore if a space was in the LDtk definition
            input = input.Replace(' ', '_');
            
            if (_enumType != null && _enumType.IsEnum)
            {
                if (Enum.IsDefined(_enumType, input))
                {
                    return Enum.Parse(_enumType, input);
                }
                
                Debug.LogError($"LDtk: C# enum \"{_enumType.Name}\" does not contain an LDtk enum value identifier \"{input}\". Name mismatch, mispelling, or undefined in LDtk editor?", LDtkInjectionErrorContext.Context);
                return default;

            }
            
            Debug.LogError($"LDtk: Invalid enum parse for enum value: {input}", LDtkInjectionErrorContext.Context);
            return default;

        }
    }
}