# Giữ các hàm giao tiếp JavascriptInterface cho WebView BookShare
-keepclassmembers class vn.bookshare.mobile.MainActivity$BookShareAndroidBridge {
    @android.webkit.JavascriptInterface <methods>;
}
