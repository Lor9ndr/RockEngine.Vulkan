namespace RockEngine.Editor.EditorUI
{
    public static class Icons
    {
        // General UI Icons
        public static readonly char Home = '\uf015';
        public static readonly char Refresh = '\uf021';
        public static readonly char Search = '\uf002';
        public static readonly char Cog = '\uf013';
        public static readonly char Bars = '\uf0c9';
        public static readonly char AngleRight = '\uf105';
        public static readonly char AngleDown = '\uf107';
        public static readonly char Check = '\uf00c';
        public static readonly char Times = '\uf00d';
        public static readonly char ExclamationTriangle = '\uf071';
        public static readonly char InfoCircle = '\uf05a';
        public static readonly char QuestionCircle = '\uf059';

        // File System Icons
        public static readonly char File = '\uf15b';
        public static readonly char Folder = '\uf07b';
        public static readonly char FolderOpen = '\uf07c';
        public static readonly char FolderPlus = '\uf65e';
        public static readonly char FileAlt = '\uf15c';
        public static readonly char FileCode = '\uf1c9';
        public static readonly char FileArchive = '\uf1c6';
        public static readonly char FileAudio = '\uf1c7';
        public static readonly char FileImage = '\uf1c5';
        public static readonly char FileVideo = '\uf1c8';
        public static readonly char FilePdf = '\uf1c1';
        public static readonly char FileWord = '\uf1c2';
        public static readonly char FileExcel = '\uf1c3';
        public static readonly char FilePowerpoint = '\uf1c4';

        // Asset Type Icons
        public static readonly char Cube = '\uf1b2';
        public static readonly char PaintBrush = '\uf53f';
        public static readonly char Code = '\uf121';
        public static readonly char Sitemap = '\uf0e8';
        public static readonly char Magic = '\uf70c';
        public static readonly char Font = '\uf031';
        public static readonly char Database = '\uf1c0';
        public static readonly char ObjectGroup = '\uf247';
        public static readonly char ObjectUngroup = '\uf248';

        // View and Layout Icons
        public static readonly char Th = '\uf00a'; // Grid view
        public static readonly char List = '\uf03a'; // List view
        public static readonly char ThLarge = '\uf009';
        public static readonly char ThList = '\uf00b';
        public static readonly char Expand = '\uf065';
        public static readonly char Compress = '\uf066';
        public static readonly char ArrowsAlt = '\uf0b2';

        // Editing and Actions
        public static readonly char Plus = '\uf067';
        public static readonly char Minus = '\uf068';
        public static readonly char Trash = '\uf1f8';
        public static readonly char Edit = '\uf044';
        public static readonly char Save = '\uf0c7';
        public static readonly char Download = '\uf019';
        public static readonly char Upload = '\uf093';
        public static readonly char Copy = '\uf0c5';
        public static readonly char Paste = '\uf0ea';
        public static readonly char Cut = '\uf0c4';
        public static readonly char Clone = '\uf24d';

        // Navigation and Arrows
        public static readonly char ArrowLeft = '\uf060';
        public static readonly char ArrowRight = '\uf061';
        public static readonly char ArrowUp = '\uf062';
        public static readonly char ArrowDown = '\uf063';
        public static readonly char ChevronLeft = '\uf053';
        public static readonly char ChevronRight = '\uf054';
        public static readonly char ChevronUp = '\uf077';
        public static readonly char ChevronDown = '\uf078';

        // Media and Graphics
        public static readonly char Image = '\uf03e';
        public static readonly char Images = '\uf302';
        public static readonly char Camera = '\uf030';
        public static readonly char Film = '\uf008';
        public static readonly char Music = '\uf001';
        public static readonly char VolumeUp = '\uf028';
        public static readonly char Play = '\uf04b';
        public static readonly char Pause = '\uf04c';
        public static readonly char Stop = '\uf04d';

        // Status and Indicators
        public static readonly char Circle = '\uf111';
        public static readonly char DotCircle = '\uf192';
        public static readonly char CheckCircle = '\uf058';
        public static readonly char TimesCircle = '\uf057';
        public static readonly char ExclamationCircle = '\uf06a';
        public static readonly char Sync = '\uf021';
        public static readonly char Spinner = '\uf110';
        public static readonly char CircleNotch = '\uf1ce';

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
        public static readonly char Wrench = '\uf0ad';
        public static readonly char Hammer = '\uf6e3';
        public static readonly char MagicWand = '\uf72c';
        public static readonly char Eye = '\uf06e';
        public static readonly char EyeSlash = '\uf070';
        public static readonly char Filter = '\uf0b0';
        public static readonly char Sort = '\uf0dc';
        public static readonly char SortAlphaDown = '\uf15d';
        public static readonly char SortAlphaUp = '\uf15e';
        public static readonly char SortNumericDown = '\uf162';
        public static readonly char SortNumericUp = '\uf163';

        // User and Settings
        public static readonly char User = '\uf007';
        public static readonly char Users = '\uf0c0';
        public static readonly char UserCog = '\uf4fe';
        public static readonly char SlidersH = '\uf1de';

        // Time and Date
        public static readonly char Clock = '\uf017';
        public static readonly char Calendar = '\uf073';
        public static readonly char CalendarAlt = '\uf073';
        public static readonly char History = '\uf1da';

        // Document and Text
        public static readonly char FileText = '\uf15c';
        public static readonly char Book = '\uf02d';
        public static readonly char BookOpen = '\uf518';
        public static readonly char Newspaper = '\uf1ea';
        public static readonly char StickyNote = '\uf249';

        // Network and Connectivity
        public static readonly char NetworkWired = '\uf6ff';
        public static readonly char Server = '\uf233';
        public static readonly char Cloud = '\uf0c2';
        public static readonly char CloudDownload = '\uf0ed';
        public static readonly char CloudUpload = '\uf0ee';

        // Special Asset Types
        public static readonly char ShieldAlt = '\uf3ed'; // For protected assets
        public static readonly char Lock = '\uf023'; // For locked assets
        public static readonly char Unlock = '\uf09c'; // For unlocked assets
        public static readonly char Star = '\uf005'; // For favorite assets
        public static readonly char Heart = '\uf004'; // For liked assets
        public static readonly char Bookmark = '\uf02e'; // For bookmarked assets

        // Preview and Visualization
        public static readonly char Desktop = '\uf108'; // Desktop view
        public static readonly char Mobile = '\uf10b'; // Mobile view
        public static readonly char Tablet = '\uf10a'; // Tablet view

        // Import/Export
        public static readonly char SignIn = '\uf090'; // Import
        public static readonly char SignOut = '\uf08b'; // Export
        public static readonly char Exchange = '\uf0ec'; // Convert

        // Version Control
        public static readonly char CodeBranch = '\uf126'; // Branch
        public static readonly char CodeCommit = '\uf386'; // Commit
        public static readonly char Tag = '\uf02b'; // Tag

        // Animation
        public static readonly char PlayCircle = '\uf144'; // Play animation
        public static readonly char StopCircle = '\uf28d'; // Stop animation
        public static readonly char FastBackward = '\uf049'; // Rewind
        public static readonly char FastForward = '\uf050'; // Fast forward
        public static readonly char StepBackward = '\uf048'; // Previous frame
        public static readonly char StepForward = '\uf051'; // Next frame

        // Lighting and Effects
        public static readonly char Lightbulb = '\uf0eb'; // Light
        public static readonly char Sun = '\uf185'; // Directional light
        public static readonly char LightbulbOn = '\uf672'; // Point light
        public static readonly char LightbulbSlash = '\uf673'; // Light off

        // Physics
        public static readonly char Atom = '\uf5d2'; // Physics
        public static readonly char Magnet = '\uf076'; // Magnet/Force
        public static readonly char Fire = '\uf06d'; // Particle effect

        // Audio
        public static readonly char WaveSquare = '\uf83e'; // Sound wave
        public static readonly char Microphone = '\uf130'; // Audio source
        public static readonly char Headphones = '\uf025'; // Audio listener

        // UI Specific
        public static readonly char BorderAll = '\uf84c'; // UI canvas
        public static readonly char VectorSquare = '\uf5cb'; // UI element
        public static readonly char DrawPolygon = '\uf5ee'; // Shape

        // Scripting
        public static readonly char Terminal = '\uf120'; // Script
        public static readonly char Bug = '\uf188'; // Debug

        // Material and Shaders
        public static readonly char FillDrip = '\uf576'; // Material
        public static readonly char Palette = '\uf53f'; // Shader
        public static readonly char Swatchbook = '\uf5c3'; // Color palette

        // Camera
        public static readonly char Video = '\uf03d'; // Video camera
        public static readonly char VRCardboard = '\uf729'; // VR camera

        // Navigation and Waypoints
        public static readonly char MapMarker = '\uf041'; // Waypoint
        public static readonly char MapMarkerAlt = '\uf3c5'; // Navigation point
        public static readonly char Compass = '\uf14e'; // Direction

        // Weather and Environment
        public static readonly char CloudSun = '\uf6c4'; // Skybox
        public static readonly char CloudMoon = '\uf6c3'; // Night sky
        public static readonly char Water = '\uf773'; // Water
        public static readonly char Mountain = '\uf6fc'; // Terrain

        // Vehicles and Objects
        public static readonly char Car = '\uf1b9'; // Vehicle
        public static readonly char Ship = '\uf21a'; // Boat
        public static readonly char Plane = '\uf072'; // Aircraft

        // Characters and Avatars
        public static readonly char UserNinja = '\uf504'; // Character
        public static readonly char Robot = '\uf544'; // AI character
        public static readonly char Ghost = '\uf6e2'; // NPC

        // Weapons and Items
        public static readonly char Crosshairs = '\uf05b'; // Weapon
        public static readonly char Gem = '\uf3a5'; // Treasure
        public static readonly char Coin = '\uf51e'; // Currency

        // Buildings and Architecture
        public static readonly char Building = '\uf1ad'; // Building
        public static readonly char Church = '\uf51d'; // Structure
        public static readonly char Tree = '\uf1bb'; // Foliage

        // Effects and Particles
        public static readonly char Smoke = '\uf75f'; // Smoke
        public static readonly char Snowflake = '\uf2dc'; // Snow

        // Tools for Asset Browser
        public static readonly char SearchPlus = '\uf00e'; // Zoom in
        public static readonly char SearchMinus = '\uf010'; // Zoom out
        public static readonly char ExpandArrowsAlt = '\uf31e'; // Fullscreen
        public static readonly char CompressArrowsAlt = '\uf78c'; // Exit fullscreen
        public static readonly char LayerGroup = '\uf5fd'; // Layers
    }
}
