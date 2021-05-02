﻿namespace LDtkUnity.Editor
{
    public delegate object ParseFieldValueAction(object input);
    
    public interface ILDtkValueParser
    {
        string TypeName { get; }
        object ParseValue(object input);
    }
}