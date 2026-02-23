namespace RockEngine.Editor.EditorUI
{
    public static class Icons
    {
        // General UI Icons
        public const char Home = '\uf015';
        public const char Refresh = '\uf021';
        public const char Search = '\uf002';
        public const char Cog = '\uf013';
        public const char Bars = '\uf0c9';
        public const char AngleRight = '\uf105';
        public const char AngleDown = '\uf107';
        public const char Check = '\uf00c';
        public const char Times = '\uf00d';
        public const char ExclamationTriangle = '\uf071';
        public const char InfoCircle = '\uf05a';
        public const char QuestionCircle = '\uf059';

        // File System Icons
        public const char File = '\uf15b';
        public const char Folder = '\uf07b';
        public const char FolderOpen = '\uf07c';
        public const char FolderPlus = '\uf65e';
        public const char FileAlt = '\uf15c';
        public const char FileCode = '\uf1c9';
        public const char FileArchive = '\uf1c6';
        public const char FileAudio = '\uf1c7';
        public const char FileImage = '\uf1c5';
        public const char FileVideo = '\uf1c8';
        public const char FilePdf = '\uf1c1';
        public const char FileWord = '\uf1c2';
        public const char FileExcel = '\uf1c3';
        public const char FilePowerpoint = '\uf1c4';

        // Asset Type Icons
        public const char Cube = '\uf1b2';
        public const char PaintBrush = '\uf53f';
        public const char Code = '\uf121';
        public const char Sitemap = '\uf0e8';
        public const char Magic = '\uf70c';
        public const char Font = '\uf031';
        public const char Database = '\uf1c0';
        public const char ObjectGroup = '\uf247';
        public const char ObjectUngroup = '\uf248';

        // View and Layout Icons
        public const char Th = '\uf00a'; // Grid view
        public const char List = '\uf03a'; // List view
        public const char ThLarge = '\uf009';
        public const char ThList = '\uf00b';
        public const char Expand = '\uf065';
        public const char Compress = '\uf066';
        public const char ArrowsAlt = '\uf0b2';

        // Editing and Actions
        public const char Plus = '\uf067';
        public const char Minus = '\uf068';
        public const char Trash = '\uf1f8';
        public const char Edit = '\uf044';
        public const char Save = '\uf0c7';
        public const char Download = '\uf019';
        public const char Upload = '\uf093';
        public const char Copy = '\uf0c5';
        public const char Paste = '\uf0ea';
        public const char Cut = '\uf0c4';
        public const char Clone = '\uf24d';

        // Navigation and Arrows
        public const char ArrowLeft = '\uf060';
        public const char ArrowRight = '\uf061';
        public const char ArrowUp = '\uf062';
        public const char ArrowDown = '\uf063';
        public const char ChevronLeft = '\uf053';
        public const char ChevronRight = '\uf054';
        public const char ChevronUp = '\uf077';
        public const char ChevronDown = '\uf078';

        // Media and Graphics
        public const char Image = '\uf03e';
        public const char Images = '\uf302';
        public const char Camera = '\uf030';
        public const char Film = '\uf008';
        public const char Music = '\uf001';
        public const char VolumeUp = '\uf028';
        public const char Play = '\uf04b';
        public const char Pause = '\uf04c';
        public const char Stop = '\uf04d';

        // Status and Indicators
        public const char Circle = '\uf111';
        public const char DotCircle = '\uf192';
        public const char CheckCircle = '\uf058';
        public const char TimesCircle = '\uf057';
        public const char ExclamationCircle = '\uf06a';
        public const char Sync = '\uf021';
        public const char Spinner = '\uf110';
        public const char CircleNotch = '\uf1ce';

        // Loading Spinner Frames (Font Awesome doesn't have built-in spinner frames, so we use these)
        public static readonly char[] SpinnerFrames =
        {
        '\uf110', // Spinner frame 1
        '\uf110', // Spinner frame 2 (Font Awesome has one spinner icon, you might want to use custom frames)
    };

        // Alternative loading spinner using dots (creative use of existing icons)
        public static readonly char[] LoadingDots =
        {
        '\uf111', // Circle
        '\uf192', // Dot Circle
        '\uf111', // Circle
    };

        // Tools and Utilities
        public const char Wrench = '\uf0ad';
        public const char Hammer = '\uf6e3';
        public const char MagicWand = '\uf72c';
        public const char Eye = '\uf06e';
        public const char EyeSlash = '\uf070';
        public const char Filter = '\uf0b0';
        public const char Sort = '\uf0dc';
        public const char SortAlphaDown = '\uf15d';
        public const char SortAlphaUp = '\uf15e';
        public const char SortNumericDown = '\uf162';
        public const char SortNumericUp = '\uf163';

        // User and Settings
        public const char User = '\uf007';
        public const char Users = '\uf0c0';
        public const char UserCog = '\uf4fe';
        public const char SlidersH = '\uf1de';

        // Time and Date
        public const char Clock = '\uf017';
        public const char Calendar = '\uf073';
        public const char CalendarAlt = '\uf073';
        public const char History = '\uf1da';

        // Document and Text
        public const char FileText = '\uf15c';
        public const char Book = '\uf02d';
        public const char BookOpen = '\uf518';
        public const char Newspaper = '\uf1ea';
        public const char StickyNote = '\uf249';

        // Network and Connectivity
        public const char NetworkWired = '\uf6ff';
        public const char Server = '\uf233';
        public const char Cloud = '\uf0c2';
        public const char CloudDownload = '\uf0ed';
        public const char CloudUpload = '\uf0ee';

        // Special Asset Types
        public const char ShieldAlt = '\uf3ed'; // For protected assets
        public const char Lock = '\uf023'; // For locked assets
        public const char Unlock = '\uf09c'; // For unlocked assets
        public const char Star = '\uf005'; // For favorite assets
        public const char Heart = '\uf004'; // For liked assets
        public const char Bookmark = '\uf02e'; // For bookmarked assets

        // Preview and Visualization
        public const char Desktop = '\uf108'; // Desktop view
        public const char Mobile = '\uf10b'; // Mobile view
        public const char Tablet = '\uf10a'; // Tablet view

        // Import/Export
        public const char SignIn = '\uf090'; // Import
        public const char SignOut = '\uf08b'; // Export
        public const char Exchange = '\uf0ec'; // Convert

        // Version Control
        public const char CodeBranch = '\uf126'; // Branch
        public const char CodeCommit = '\uf386'; // Commit
        public const char Tag = '\uf02b'; // Tag

        // Animation
        public const char PlayCircle = '\uf144'; // Play animation
        public const char StopCircle = '\uf28d'; // Stop animation
        public const char FastBackward = '\uf049'; // Rewind
        public const char FastForward = '\uf050'; // Fast forward
        public const char StepBackward = '\uf048'; // Previous frame
        public const char StepForward = '\uf051'; // Next frame

        // Lighting and Effects
        public const char Lightbulb = '\uf0eb'; // Light
        public const char Sun = '\uf185'; // Directional light
        public const char LightbulbOn = '\uf672'; // Point light
        public const char LightbulbSlash = '\uf673'; // Light off

        // Physics
        public const char Atom = '\uf5d2'; // Physics
        public const char Magnet = '\uf076'; // Magnet/Force
        public const char Fire = '\uf06d'; // Particle effect

        // Audio
        public const char WaveSquare = '\uf83e'; // Sound wave
        public const char Microphone = '\uf130'; // Audio source
        public const char Headphones = '\uf025'; // Audio listener

        // UI Specific
        public const char BorderAll = '\uf84c'; // UI canvas
        public const char VectorSquare = '\uf5cb'; // UI element
        public const char DrawPolygon = '\uf5ee'; // Shape

        // Scripting
        public const char Terminal = '\uf120'; // Script
        public const char Bug = '\uf188'; // Debug

        // Material and Shaders
        public const char FillDrip = '\uf576'; // Material
        public const char Palette = '\uf53f'; // Shader
        public const char Swatchbook = '\uf5c3'; // Color palette

        // Camera
        public const char Video = '\uf03d'; // Video camera
        public const char VRCardboard = '\uf729'; // VR camera

        // Navigation and Waypoints
        public const char MapMarker = '\uf041'; // Waypoint
        public const char MapMarkerAlt = '\uf3c5'; // Navigation point
        public const char Compass = '\uf14e'; // Direction

        // Weather and Environment
        public const char CloudSun = '\uf6c4'; // Skybox
        public const char CloudMoon = '\uf6c3'; // Night sky
        public const char Water = '\uf773'; // Water
        public const char Mountain = '\uf6fc'; // Terrain

        // Vehicles and Objects
        public const char Car = '\uf1b9'; // Vehicle
        public const char Ship = '\uf21a'; // Boat
        public const char Plane = '\uf072'; // Aircraft

        // Characters and Avatars
        public const char UserNinja = '\uf504'; // Character
        public const char Robot = '\uf544'; // AI character
        public const char Ghost = '\uf6e2'; // NPC

        // Weapons and Items
        public const char Crosshairs = '\uf05b'; // Weapon
        public const char Gem = '\uf3a5'; // Treasure
        public const char Coin = '\uf51e'; // Currency

        // Buildings and Architecture
        public const char Building = '\uf1ad'; // Building
        public const char Church = '\uf51d'; // Structure
        public const char Tree = '\uf1bb'; // Foliage

        // Effects and Particles
        public const char Smoke = '\uf75f'; // Smoke
        public const char Snowflake = '\uf2dc'; // Snow

        // Tools for Asset Browser
        public const char SearchPlus = '\uf00e'; // Zoom in
        public const char SearchMinus = '\uf010'; // Zoom out
        public const char ExpandArrowsAlt = '\uf31e'; // Fullscreen
        public const char CompressArrowsAlt = '\uf78c'; // Exit fullscreen
        public const char LayerGroup = '\uf5fd'; // Layers
        public const char Close = '\uf2d3';
    }
}
