package vn.bookshare.mobile;

import android.Manifest;
import android.annotation.SuppressLint;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.view.View;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.ProgressBar;

import androidx.activity.OnBackPressedCallback;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.browser.customtabs.CustomTabsIntent;
import androidx.core.content.ContextCompat;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import java.util.Locale;

public class MainActivity extends AppCompatActivity {

    public static final String EXTRA_TARGET_URL = "extra_target_url";

    private WebView webView;
    private SwipeRefreshLayout swipeRefresh;
    private ProgressBar progressBar;

    private PermissionRequest pendingWebPermissionRequest;
    private ValueCallback<Uri[]> pendingFilePathCallback;

    private final ActivityResultLauncher<String> cameraPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), granted -> {
                if (pendingWebPermissionRequest != null) {
                    if (granted) {
                        pendingWebPermissionRequest.grant(pendingWebPermissionRequest.getResources());
                    } else {
                        pendingWebPermissionRequest.deny();
                    }
                    pendingWebPermissionRequest = null;
                }
            });

    private final ActivityResultLauncher<String> notificationPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), ignored -> { });

    private final ActivityResultLauncher<Intent> fileChooserLauncher =
            registerForActivityResult(new ActivityResultContracts.StartActivityForResult(), result -> {
                if (pendingFilePathCallback == null) {
                    return;
                }
                Uri[] results = WebChromeClient.FileChooserParams.parseResult(
                        result.getResultCode(),
                        result.getData()
                );
                pendingFilePathCallback.onReceiveValue(results);
                pendingFilePathCallback = null;
            });

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        swipeRefresh = findViewById(R.id.swipeRefresh);
        webView = findViewById(R.id.bookShareWebView);
        progressBar = findViewById(R.id.webProgressBar);

        swipeRefresh.setColorSchemeResources(R.color.bookshare_primary);
        swipeRefresh.setOnRefreshListener(() -> webView.reload());

        configureWebView();
        setupBackNavigation();
        requestNotificationPermissionIfNeeded();
        NotificationPoller.schedulePeriodic(this);

        webView.loadUrl(resolveUrlFromIntent(getIntent()));
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String targetUrl = resolveUrlFromIntent(intent);
        if (webView != null && targetUrl != null && !targetUrl.isEmpty()) {
            webView.loadUrl(targetUrl);
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        CookieManager.getInstance().flush();
    }

    @Override
    protected void onResume() {
        super.onResume();
        NotificationPoller.triggerNow(this);
    }

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    private void configureWebView() {
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
        settings.setUserAgentString(settings.getUserAgentString() + " BookShareAndroidApp/1.0");

        CookieManager cookieManager = CookieManager.getInstance();
        cookieManager.setAcceptCookie(true);
        cookieManager.setAcceptThirdPartyCookies(webView, true);

        webView.addJavascriptInterface(new BookShareAndroidBridge(), "BookShareAndroidBridge");

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                String url = uri.toString();

                if (shouldOpenExternalOAuth(uri)) {
                    openCustomTab(url);
                    return true;
                }

                if (isSameOrigin(uri)) {
                    return false;
                }

                try {
                    Intent external = new Intent(Intent.ACTION_VIEW, uri);
                    startActivity(external);
                } catch (ActivityNotFoundException ignored) {
                    // Không có trình duyệt phù hợp trên thiết bị
                }
                return true;
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                swipeRefresh.setRefreshing(false);
                progressBar.setVisibility(View.GONE);
                CookieManager.getInstance().flush();
                NotificationPoller.triggerNow(MainActivity.this);
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame()) {
                    swipeRefresh.setRefreshing(false);
                    progressBar.setVisibility(View.GONE);
                    view.loadUrl("file:///android_asset/offline.html");
                }
            }
        });

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView view, int newProgress) {
                if (newProgress < 100) {
                    progressBar.setVisibility(View.VISIBLE);
                    progressBar.setProgress(newProgress);
                } else {
                    progressBar.setVisibility(View.GONE);
                }
            }

            @Override
            public void onPermissionRequest(PermissionRequest request) {
                runOnUiThread(() -> {
                    if (!isSameOrigin(request.getOrigin())) {
                        request.deny();
                        return;
                    }
                    boolean asksCamera = false;
                    for (String res : request.getResources()) {
                        if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(res)) {
                            asksCamera = true;
                            break;
                        }
                    }
                    if (!asksCamera) {
                        request.deny();
                        return;
                    }
                    if (ContextCompat.checkSelfPermission(MainActivity.this, Manifest.permission.CAMERA)
                            == PackageManager.PERMISSION_GRANTED) {
                        request.grant(request.getResources());
                    } else {
                        pendingWebPermissionRequest = request;
                        cameraPermissionLauncher.launch(Manifest.permission.CAMERA);
                    }
                });
            }

            @Override
            public boolean onShowFileChooser(
                    WebView webView,
                    ValueCallback<Uri[]> filePathCallback,
                    FileChooserParams fileChooserParams) {
                if (pendingFilePathCallback != null) {
                    pendingFilePathCallback.onReceiveValue(null);
                }
                pendingFilePathCallback = filePathCallback;

                try {
                    Intent chooser = fileChooserParams.createIntent();
                    fileChooserLauncher.launch(Intent.createChooser(chooser, "Chọn ảnh hoặc tệp"));
                    return true;
                } catch (ActivityNotFoundException e) {
                    pendingFilePathCallback = null;
                    return false;
                }
            }
        });
    }

    private void setupBackNavigation() {
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                if (webView != null && webView.canGoBack()) {
                    webView.goBack();
                } else {
                    setEnabled(false);
                    getOnBackPressedDispatcher().onBackPressed();
                }
            }
        });
    }

    private void requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
                ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS);
        }
    }

    private boolean isSameOrigin(Uri uri) {
        if (uri == null) {
            return false;
        }
        Uri baseUri = Uri.parse(BuildConfig.BASE_URL);
        String host = uri.getHost();
        String baseHost = baseUri.getHost();
        return host != null && baseHost != null && host.equalsIgnoreCase(baseHost);
    }

    private boolean shouldOpenExternalOAuth(Uri uri) {
        if (uri == null) {
            return false;
        }
        String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
        String path = uri.getPath() == null ? "" : uri.getPath().toLowerCase(Locale.ROOT);
        return host.equals("accounts.google.com")
                || host.endsWith(".accounts.google.com")
                || (isSameOrigin(uri) && (path.contains("/s01authotpgoogle/googlelogin") || path.contains("/signin-google")));
    }

    private void openCustomTab(String url) {
        if (url == null || (!url.startsWith("https://") && !url.startsWith("http://"))) {
            return;
        }
        try {
            CustomTabsIntent customTabsIntent = new CustomTabsIntent.Builder().build();
            customTabsIntent.launchUrl(this, Uri.parse(url));
        } catch (ActivityNotFoundException ignored) {
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
            } catch (ActivityNotFoundException ignoredAgain) {
                // Bỏ qua nếu thiết bị không có trình duyệt
            }
        }
    }

    @NonNull
    private String resolveUrlFromIntent(Intent intent) {
        if (intent != null) {
            String extra = intent.getStringExtra(EXTRA_TARGET_URL);
            if (extra != null && !extra.trim().isEmpty()) {
                String trimmed = extra.trim();
                if (trimmed.startsWith("/")) {
                    return BuildConfig.BASE_URL + trimmed;
                }
                Uri parsedExtra = Uri.parse(trimmed);
                if (isSameOrigin(parsedExtra)) {
                    return trimmed;
                }
            }

            Uri data = intent.getData();
            if (data != null && "bookshare".equalsIgnoreCase(data.getScheme()) && "open".equalsIgnoreCase(data.getHost())) {
                String path = data.getQueryParameter("path");
                if (path != null && path.startsWith("/")) {
                    return BuildConfig.BASE_URL + path;
                }
            }
        }
        return BuildConfig.BASE_URL;
    }

    public class BookShareAndroidBridge {
        @JavascriptInterface
        public boolean isNativeApp() {
            return true;
        }

        @JavascriptInterface
        public String getAppInfo() {
            return "{\"platform\":\"android\",\"version\":\"3.0.0\"}";
        }

        @JavascriptInterface
        public void vibrateShort() {
            Vibrator vibrator = getSystemService(Vibrator.class);
            if (vibrator != null && vibrator.hasVibrator()) {
                vibrator.vibrate(VibrationEffect.createOneShot(45, VibrationEffect.DEFAULT_AMPLITUDE));
            }
        }

        @JavascriptInterface
        public void openExternalAuth(String url) {
            runOnUiThread(() -> openCustomTab(url));
        }

        @JavascriptInterface
        public void syncNotificationsNow() {
            NotificationPoller.triggerNow(MainActivity.this);
        }

        @JavascriptInterface
        public void pushLocalNotification(String title, String message, String targetUrl) {
            NotificationPoller.showNativeNotification(
                    MainActivity.this,
                    (int) (System.currentTimeMillis() & 0x7fffffff),
                    title != null ? title : "Thông báo BookShare",
                    message != null ? message : "",
                    targetUrl
            );
        }

        @JavascriptInterface
        public void reloadHome() {
            runOnUiThread(() -> {
                if (webView != null) {
                    webView.loadUrl(BuildConfig.BASE_URL);
                }
            });
        }
    }
}
