﻿using System;

namespace QuestPatcher.Core.Patching
{
    public class PatchingException : Exception
    {
        public PatchingException(string message) : base(message) { }
        public PatchingException(string? message, Exception cause) : base(message, cause) { }
    }

    public class GameNotInstalledException : PatchingException
    {
        public GameNotInstalledException(string message) : base(message) { }
    }
    
    public class GameIsCrackedException : PatchingException
    {
        public GameIsCrackedException(string message) : base(message) { }
    }
}
