plugins {
  alias(libs.plugins.android.application)
  alias(libs.plugins.compose.compiler)
  alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "app.krate"
    compileSdk = 37
    defaultConfig {
        applicationId = "app.krate"
        minSdk = 24
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            // R8 on, resources shrunk with it. The Compose/JNA keep rules live in proguard-rules.pro
            // — JNA binds by reflection, so its interfaces cannot be renamed.
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
        debug {
            // So a debug build can sit beside a release one on the same device.
            applicationIdSuffix = ".debug"
            versionNameSuffix = "-debug"
        }
    }

    // The Rust core is ~9 MB per ABI and we ship three, so a universal APK makes every device carry
    // ~18 MB of machine code it can never execute. Splitting emits one APK per ABI instead.
    // (Publishing an .aab makes Play do this automatically; splits are for direct distribution.)
    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a", "x86_64")
            isUniversalApk = false
        }
    }

    packaging {
        resources {
            // Duplicate licence/metadata entries from transitive jars, none of them needed at run time.
            excludes += setOf(
                "/META-INF/{AL2.0,LGPL2.1}",
                "/META-INF/DEPENDENCIES",
                "/META-INF/*.kotlin_module",
                "kotlin/**",
                "**/*.txt",
            )
        }
        jniLibs {
            // youtubedl-android ships its Python and ffmpeg payloads as *.zip.so — zip archives
            // with an .so extension, so the packaging step will carry them. They are not ELF, so
            // llvm-strip must be told to leave them alone or the build fails on every ABI.
            keepDebugSymbols += setOf("**/libpython.zip.so", "**/libffmpeg.zip.so")
            // The library extracts those payloads at runtime, which needs legacy (compressed)
            // packaging; with the modern uncompressed layout it cannot unpack them.
            useLegacyPackaging = true
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    buildFeatures {
      compose = true
      aidl = false
      buildConfig = false
      shaders = false
    }

    packaging {
      resources {
        excludes += "/META-INF/{AL2.0,LGPL2.1}"
      }
    }
}

kotlin {
    jvmToolchain(17)
}

tasks.register<Exec>("buildRust") {
    workingDir = rootProject.file("../rust")
    
    // Fallback to cargo if command doesn't exist to avoid breaking sync completely
    commandLine(
        "cmd", "/c", "cargo", "ndk", 
        "-t", "arm64-v8a", 
        "-t", "armeabi-v7a", 
        "-t", "x86_64", 
        "-o", "../app/app/src/main/jniLibs", 
        "build", "--release"
    )
}

tasks.named("preBuild") {
    // dependsOn("buildRust") // disabled until cargo-ndk is installed
}

dependencies {
  val composeBom = platform(libs.androidx.compose.bom)
  implementation(composeBom)
  androidTestImplementation(composeBom)

  // Core Android dependencies
  implementation(libs.androidx.core.ktx)
  implementation(libs.androidx.lifecycle.runtime.ktx)
  implementation(libs.androidx.activity.compose)

  // Arch Components
  implementation(libs.androidx.lifecycle.runtime.compose)
  implementation(libs.androidx.lifecycle.viewmodel.compose)

  // Compose
  implementation(libs.androidx.compose.ui)
  implementation(libs.androidx.compose.ui.tooling.preview)
  implementation(libs.androidx.compose.material3)
  
  // JNA for Rust FFI
  implementation("net.java.dev.jna:jna:5.13.0@aar")

  // Tooling
  debugImplementation(libs.androidx.compose.ui.tooling)
  // Instrumented tests
  androidTestImplementation(libs.androidx.compose.ui.test.junit4)
  debugImplementation(libs.androidx.compose.ui.test.manifest)

  // Local tests: jUnit, coroutines, Android runner
  testImplementation(libs.junit)
  testImplementation(libs.kotlinx.coroutines.test)

  // Instrumented tests: jUnit rules and runners
  androidTestImplementation(libs.androidx.test.core)
  androidTestImplementation(libs.androidx.test.ext.junit)
  androidTestImplementation(libs.androidx.test.runner)
  androidTestImplementation(libs.androidx.test.espresso.core)

  // Navigation
  implementation("androidx.navigation:navigation-compose:2.8.0")
  // navigation3 removed with the dead template chain it served: MainActivity navigates with
  // navigation-compose above. See app/_unused-template/.
  implementation("androidx.compose.material:material-icons-extended")
  implementation(libs.youtubedl.android)
  implementation(libs.youtubedl.ffmpeg)
  
  // QR Scanner
  implementation("com.journeyapps:zxing-android-embedded:4.3.0")
  
  // Image Loading
  implementation("io.coil-kt:coil-compose:2.6.0")
}

