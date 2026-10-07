// Copyright (c) NeeLaboratory.
using System;

namespace NeeView
{
    public abstract class PropertyMapNode
    {
        protected PropertyMapNode(string name, ObsoleteAttribute? obsolete, AlternativeAttribute? alternative)
        {
            Name = name;
            Obsolete = obsolete;
            Alternative = alternative;
        }

        public string Name { get; }
        public ObsoleteAttribute? Obsolete { get; }
        public AlternativeAttribute? Alternative { get; }

        public bool IsObsolete => Obsolete != null;

        public string? CreateObsoleteMessage()
        {
            return $"{Name} is obsolete." + (Alternative?.Alternative is { } target ? $" Use {target}." : "");
        }
    }
}
