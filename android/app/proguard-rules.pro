# Ktor / Netty
-keep class io.netty.** { *; }
-keep class io.ktor.** { *; }
-dontwarn io.netty.**
-dontwarn com.sun.nio.file.**
-dontwarn java.lang.management.**
-dontwarn reactor.blockhound.**

# kotlinx-serialization
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**
-keepclassmembers class kotlinx.serialization.json.** { *** Companion; }
-keepclasseswithmembers class kotlinx.serialization.json.** {
    kotlinx.serialization.KSerializer serializer(...);
}
-keep,includedescriptorclasses class dev.davidfdev.fling.**$$serializer { *; }
-keepclassmembers class dev.davidfdev.fling.** {
    *** Companion;
}
-keepclasseswithmembers class dev.davidfdev.fling.** {
    kotlinx.serialization.KSerializer serializer(...);
}
