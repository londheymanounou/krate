
# --- KRATE ---------------------------------------------------------------
# JNA maps these by *reflection*: the interface methods must keep their exact
# names to match the exported C symbols, and Structure subclasses need their
# fields intact for FieldOrder to resolve. R8 renaming any of it produces a
# runtime UnsatisfiedLinkError that no build-time check catches.
-keep class com.sun.jna.** { *; }
-keepclassmembers class * extends com.sun.jna.** { *; }
-keep interface app.krate.KrateCore { *; }
-keep class app.krate.KrateResult { *; }
-keep class app.krate.KrateResult$ByValue { *; }

# JNA ships optional bindings for platforms we do not build for.
-dontwarn java.awt.**
-dontwarn com.sun.jna.**
