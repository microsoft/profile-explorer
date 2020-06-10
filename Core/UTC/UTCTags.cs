// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Core.UTC {
    public enum SymbolAnnotationKind {
        Volatile,     // ^
        Writethrough, // ~
        CantMakeSDSU, // -
        Dead          // !
    }

    public class SymbolAnnotationTag {
        //? TODO
    }

}
