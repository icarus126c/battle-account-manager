namespace BrowserManager {
    public static class Edition {
#if EDITION_SPONSORED
        public static string Name { get { return "赞助版"; } }
        public static bool Sponsorship { get { return true; } }
#elif EDITION_FRIENDS
        public static string Name { get { return "友情分享版"; } }
        public static bool Sponsorship { get { return false; } }
#else
        public static string Name { get { return "自用版"; } }
        public static bool Sponsorship { get { return false; } }
#endif
    }
}
