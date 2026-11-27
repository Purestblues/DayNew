# kotlinx.serialization DTOs & generated Companion / $serializer
-keepattributes *Annotation*, InnerClasses
-keepclassmembers class kotlinx.serialization.json.** { *** Companion; }
-keepclasseswithmembers class kotlinx.serialization.json.** { kotlinx.serialization.KSerializer serializer(...); }

-keep,includedescriptorclasses class com.daynew.sync.** { *; }
-keep,includedescriptorclasses class com.daynew.app.sync.** { *; }
-keep,includedescriptorclasses class com.daynew.app.alarm.** { *; }
-keep,includedescriptorclasses class com.daynew.app.widget.** { *; }

# Keep Android entry points registered in AndroidManifest.xml
-keep public class * extends android.app.Application
-keep public class * extends android.app.Activity
-keep public class * extends android.content.BroadcastReceiver
-keep public class * extends androidx.work.CoroutineWorker
-keep public class * extends androidx.glance.appwidget.GlanceAppWidgetReceiver

# OkHttp / Coroutines / XML parser optional desktop JDK classes
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn javax.naming.**
-dontwarn org.codehaus.mojo.animal_sniffer.**
