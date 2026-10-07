// Copyright (c) NeeLaboratory. MIT. 迁自 NeeView/NeeView/Numetrics/IRational.cs，原纯算法保持。
namespace NeeView.Numetrics
{
    public interface IRational
    {
        double ToValue();
        string ToRationalString();

        IRational Reduction();
    }
}
