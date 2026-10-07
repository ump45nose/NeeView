// Copyright (c) NeeLaboratory. MIT. 迁自 NeeView/NeeView/Media/Imaging/Metadata/BitmapMetadataAccessor.cs，保留原元数据算法。
using System.Collections.Generic;

namespace NeeView.Media.Imaging.Metadata
{
    public abstract class BitmapMetadataAccessor
    {
        public abstract string GetFormat();

        public abstract object? GetValue(BitmapMetadataKey key);

        public virtual Dictionary<string, object?> GetExtraValues()
        {
            return new();
        }
    }
}
