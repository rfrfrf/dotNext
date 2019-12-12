﻿namespace DotNext.IO
{
    /// <summary>
    /// Describes how string length should be encoded in binary form.
    /// </summary>
    public enum StringLengthEncoding : byte
    {
        /// <summary>
        /// Use 4-byte to represent string length.
        /// </summary>
        Plain,

        /// <summary>
        /// Use 7-bit encoding compressed format.
        /// </summary>
        SevenBitEncoded
    }
}