package app.krate

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.ui.graphics.vector.ImageVector

/**
 * One icon per tool.
 *
 * Keyed on the tool **id**, which is the stable unlocalized identifier from the core's catalogue —
 * never on the name or the localized category, both of which change with the active language.
 *
 * Per-tool rather than per-category on purpose: the category header already names the group, so a
 * repeated category icon down 26 rows of "Text" carries no information and reads as unfinished.
 * Unmapped ids fall back to the category icon, so adding a tool to the core degrades gracefully
 * instead of breaking the build.
 */
private val ICONS: Map<String, ImageVector> = mapOf(
    // Colors
    "Color" to Icons.Rounded.Colorize,
    "ColorBlind" to Icons.Rounded.Visibility,
    "ColorTemp" to Icons.Rounded.Thermostat,
    "Contrast" to Icons.Rounded.Contrast,
    "CssUnits" to Icons.Rounded.Straighten,
    "Gradient" to Icons.Rounded.Gradient,
    "Palette" to Icons.Rounded.Palette,

    // Conversions
    "Convert" to Icons.Rounded.SwapHoriz,
    "Currency" to Icons.Rounded.CurrencyExchange,
    "Roman" to Icons.Rounded.Numbers,
    "ShoeSize" to Icons.Rounded.DoNotStep,
    "SpeedDistanceTime" to Icons.Rounded.Speed,
    "Spell" to Icons.Rounded.Spellcheck,
    "TransferTime" to Icons.Rounded.CloudDownload,

    // Dates
    "Clock" to Icons.Rounded.AccessTime,
    "TimerStopwatch" to Icons.Rounded.Timer,
    "DateDiff" to Icons.Rounded.DateRange,
    "Duration" to Icons.Rounded.Timelapse,
    "Timestamp" to Icons.Rounded.Schedule,
    "Timezone" to Icons.Rounded.Public,
    "WeekInfo" to Icons.Rounded.CalendarMonth,

    // Developer
    "Barcode" to Icons.Rounded.ViewWeek,
    "Chmod" to Icons.Rounded.Lock,
    "Crlf" to Icons.Rounded.KeyboardReturn,
    "CssMinify" to Icons.Rounded.Compress,
    "CurlToCode" to Icons.Rounded.Terminal,
    "DnsLookup" to Icons.Rounded.Dns,
    "EnvVars" to Icons.Rounded.Settings,
    "Gitignore" to Icons.Rounded.RemoveCircleOutline,
    "HexDump" to Icons.Rounded.Memory,
    "HttpStatus" to Icons.Rounded.Http,
    "JsonFormat" to Icons.Rounded.DataObject,
    "JsonMinify" to Icons.Rounded.Compress,
    "JsonValidate" to Icons.Rounded.CheckCircle,
    "Lf" to Icons.Rounded.KeyboardReturn,
    "MimeType" to Icons.Rounded.Description,
    "PortLookup" to Icons.Rounded.SettingsEthernet,
    "Qr" to Icons.Rounded.QrCode2,
    "QueryString" to Icons.Rounded.QuestionMark,
    "Regex" to Icons.Rounded.Pattern,
    "SqlFormat" to Icons.Rounded.Storage,
    "UrlParse" to Icons.Rounded.Link,
    "XmlFormat" to Icons.Rounded.Code,
    "XmlValidate" to Icons.Rounded.Rule,

    // Encoding
    "Base64" to Icons.Rounded.Lock,
    "Base64Decode" to Icons.Rounded.LockOpen,
    "Bases" to Icons.Rounded.Calculate,
    "Cron" to Icons.Rounded.Alarm,
    "CsvToJson" to Icons.Rounded.TableChart,
    "HtmlDecode" to Icons.Rounded.CodeOff,
    "HtmlEncode" to Icons.Rounded.Html,
    "JsonEscape" to Icons.Rounded.FormatQuote,
    "JsonToCsv" to Icons.Rounded.GridOn,
    "JsonToYaml" to Icons.Rounded.List,
    "JsonUnescape" to Icons.Rounded.FormatQuote,
    "Jwt" to Icons.Rounded.VpnKey,
    "MarkdownToHtml" to Icons.Rounded.Article,
    "Scientific" to Icons.Rounded.Science,
    "ShellEscape" to Icons.Rounded.Terminal,
    "SqlEscape" to Icons.Rounded.Storage,
    "UrlDecode" to Icons.Rounded.LinkOff,
    "UrlEncode" to Icons.Rounded.AddLink,

    // Everyday
    "Bmi" to Icons.Rounded.MonitorWeight,
    "Game2048" to Icons.Rounded.Grid4x4,
    "Loan" to Icons.Rounded.AccountBalance,
    "Snake" to Icons.Rounded.VideogameAsset,
    "Game2048" to Icons.Rounded.Grid4x4,
    "Tetris" to Icons.Rounded.ViewComfy,
    "Subnet" to Icons.Rounded.Router,
    "SysInfo" to Icons.Rounded.PhoneAndroid,
    "Tetris" to Icons.Rounded.Extension,
    "Tip" to Icons.Rounded.Restaurant,
    "Weather" to Icons.Rounded.WbSunny,

    // Files
    "Duplicates" to Icons.Rounded.FileCopy,
    "FileCompare" to Icons.Rounded.Difference,
    "FileHash" to Icons.Rounded.Fingerprint,
    "FileJoin" to Icons.Rounded.MergeType,
    "FileSplit" to Icons.Rounded.CallSplit,
    "FilenameClean" to Icons.Rounded.CleaningServices,
    "FolderSize" to Icons.Rounded.FolderOpen,
    "PathConvert" to Icons.Rounded.AltRoute,
    "PdfMerge" to Icons.Rounded.PictureAsPdf,
    "PdfSplit" to Icons.Rounded.ContentCut,
    "Rename" to Icons.Rounded.DriveFileRenameOutline,
    "TestFile" to Icons.Rounded.NoteAdd,
    "Tree" to Icons.Rounded.AccountTree,
    "Unzip" to Icons.Rounded.FolderZip,
    "Zip" to Icons.Rounded.Archive,

    // Android-only sensor tools
    "Compass" to Icons.Rounded.Explore,
    "Accelerometer" to Icons.Rounded.Speed,
    "Gyroscope" to Icons.Rounded.ScreenRotation,
    "Magnetometer" to Icons.Rounded.Sensors,
    "Ruler" to Icons.Rounded.Straighten,
    "Gamepad" to Icons.Rounded.SportsEsports,
    "SoundTester" to Icons.Rounded.Hearing,
    "Downloader" to Icons.Rounded.Download,
    "FileConverter" to Icons.Rounded.Transform,
    "Tally" to Icons.Rounded.PlusOne,

    // Hashing
    "Decrypt" to Icons.Rounded.LockOpen,
    "Encrypt" to Icons.Rounded.EnhancedEncryption,
    "HashAll" to Icons.Rounded.Tag,
    "Md5" to Icons.Rounded.Tag,
    "Password" to Icons.Rounded.Password,
    "PasswordStrength" to Icons.Rounded.Security,
    "Sha1" to Icons.Rounded.Tag,
    "Sha256" to Icons.Rounded.Tag,
    "Sha512" to Icons.Rounded.Tag,
    "Uuid" to Icons.Rounded.Fingerprint,

    // Images
    "AspectRatio" to Icons.Rounded.AspectRatio,
    "Exif" to Icons.Rounded.Info,
    "ImageInfo" to Icons.Rounded.Photo,
    "StripMetadata" to Icons.Rounded.HideImage,

    // Maths
    "Calc" to Icons.Rounded.Calculate,
    "Combinatorics" to Icons.Rounded.Functions,
    "Factor" to Icons.Rounded.Numbers,
    "Fraction" to Icons.Rounded.Percent,
    "Percent" to Icons.Rounded.Percent,
    "Sequence" to Icons.Rounded.ShowChart,
    "Solve" to Icons.Rounded.Functions,
    "Statistics" to Icons.Rounded.BarChart,

    // Random
    "Cards" to Icons.Rounded.Style,
    "Coin" to Icons.Rounded.MonetizationOn,
    "Dice" to Icons.Rounded.Casino,
    "Pick" to Icons.Rounded.TouchApp,
    "Random" to Icons.Rounded.Shuffle,
    "RandomColor" to Icons.Rounded.Palette,
    "Shuffle" to Icons.Rounded.Shuffle,
    "Teams" to Icons.Rounded.Groups,

    // Text
    "CaseConverter" to Icons.Rounded.TextFields,
    "Clean" to Icons.Rounded.CleaningServices,
    "Count" to Icons.Rounded.Pin,
    "Deaccent" to Icons.Rounded.Translate,
    "Dedupe" to Icons.Rounded.FilterAlt,
    "Diff" to Icons.Rounded.Difference,
    "Fancy" to Icons.Rounded.AutoAwesome,
    "FrenchTypography" to Icons.Rounded.Flag,
    "Inspector" to Icons.Rounded.Search,
    "Invert" to Icons.Rounded.SwapVert,
    "Lorem" to Icons.Rounded.Notes,
    "Lower" to Icons.Rounded.TextDecrease,
    "MarkdownTable" to Icons.Rounded.TableRows,
    "Mask" to Icons.Rounded.VisibilityOff,
    "Morse" to Icons.Rounded.Podcasts,
    "Naming" to Icons.Rounded.DriveFileRenameOutline,
    "Reverse" to Icons.Rounded.SwapHoriz,
    "ReverseLines" to Icons.Rounded.SwapVert,
    "Slug" to Icons.Rounded.Link,
    "SortByLength" to Icons.Rounded.SortByAlpha,
    "SortLines" to Icons.Rounded.Sort,
    "Title" to Icons.Rounded.Title,
    "Toc" to Icons.Rounded.Toc,
    "Upper" to Icons.Rounded.TextIncrease,
    "WordFrequency" to Icons.Rounded.Leaderboard,
    "Zalgo" to Icons.Rounded.Whatshot,
)

/** Icon for a tool, falling back to its category's icon when the id is not mapped. */
fun toolIcon(id: String, categoryKey: String): ImageVector =
    ICONS[id] ?: categoryIcon(categoryKey)
