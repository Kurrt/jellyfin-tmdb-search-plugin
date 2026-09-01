/**
 * jellyfin-web item-details hint for TMDB/Gelato stubs.
 *
 * Must not rewrite ApiClient.getItem Fields. Jellyfin's Fields= list replaces
 * the default DTO set, so ChildCount-only requests strip Overview and other
 * metadata. Playback uses the original getItem / PlaybackInfo path.
 * Placeholder Path=/stub sources are never treated as playable.
 */
(function () {
    var STYLE_ID = 'tmdbsearch-async-streams-css';
    var SPINNER_CLASS = 'tmdbsearch-sources-loading';
    var EMPTY_CLASS = 'tmdbsearch-no-streams';
    var PATCH_FLAG = '_tmdbsearchGetItemPatched';
    var ORIGINAL_FLAG = '_tmdbsearchOriginalGetItem';

    /**
     * Injects spinner keyframes once per document.
     */
    function injectCss() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = '@keyframes tmdbsearch-spin { to { transform: rotate(360deg); } }';

        var host = document.head || document.documentElement;
        if (host) {
            host.appendChild(style);
        }
    }

    /**
     * Reads a provider id with case-insensitive key matching.
     *
     * @param {object} item Jellyfin item DTO.
     * @param {string} key Provider name such as Stremio.
     * @returns {string} Provider id or empty string.
     */
    function providerId(item, key) {
        var ids = item && item.ProviderIds;
        if (!ids) {
            return '';
        }

        if (ids[key]) {
            return String(ids[key]);
        }

        var lower = key.toLowerCase();
        for (var name in ids) {
            if (Object.prototype.hasOwnProperty.call(ids, name) && name.toLowerCase() === lower) {
                return String(ids[name]);
            }
        }

        return '';
    }

    /**
     * Returns true when a media source is the TMDB search placeholder, not a real stream.
     *
     * @param {object} source MediaSourceInfo.
     * @returns {boolean} True when Path is the stub sentinel.
     */
    function isPlaceholderSource(source) {
        return !!(source && source.Path === '/stub');
    }

    /**
     * Returns true when the DTO has at least one non-placeholder media source.
     *
     * @param {object} item Jellyfin item DTO.
     * @returns {boolean} True when playback can use a real source.
     */
    function hasPlayableSources(item) {
        var sources = item && item.MediaSources;
        if (!sources || !sources.length) {
            return false;
        }

        for (var i = 0; i < sources.length; i++) {
            if (!isPlaceholderSource(sources[i])) {
                return true;
            }
        }

        return false;
    }

    /**
     * Returns true when the item is a Gelato/TMDB stub that may lack real streams.
     *
     * @param {object} item Jellyfin item DTO.
     * @returns {boolean} True when a placeholder hint may be shown.
     */
    function needsAsyncStreams(item) {
        if (!item) {
            return false;
        }

        if (providerId(item, 'Stremio')) {
            return true;
        }

        var locationType = item.LocationType;
        if (locationType === 'Virtual' || locationType === 3) {
            return true;
        }

        var sources = item.MediaSources;
        if (!sources || !sources.length) {
            return false;
        }

        for (var i = 0; i < sources.length; i++) {
            if (isPlaceholderSource(sources[i])) {
                return true;
            }
        }

        return false;
    }

    /**
     * Finds the visible details-page content root.
     *
     * @returns {Element|null} Visible `.detailPagePrimaryContent`, if any.
     */
    function getDetailsPage() {
        var all = document.querySelectorAll('.detailPagePrimaryContainer');
        for (var i = 0; i < all.length; i++) {
            if (all[i].offsetParent !== null) {
                return all[i].querySelector('.detailPagePrimaryContent');
            }
        }

        return null;
    }

    /**
     * True when the current URL belongs to this item's detail page.
     *
     * @param {string} itemId Item GUID, with or without dashes.
     * @returns {boolean} True when the item id appears in the location.
     */
    function isCurrentPage(itemId) {
        var href = location.href;
        var noDash = String(itemId).replace(/-/g, '');
        return href.indexOf(itemId) !== -1 || href.indexOf(noDash) !== -1;
    }

    /**
     * Removes spinner and empty-state nodes from the track panel.
     *
     * @param {Element} page Details content root.
     */
    function removeStatus(page) {
        var spinner = page.querySelector('.' + SPINNER_CLASS);
        if (spinner && spinner.parentNode) {
            spinner.parentNode.removeChild(spinner);
        }

        var empty = page.querySelector('.' + EMPTY_CLASS);
        if (empty && empty.parentNode) {
            empty.parentNode.removeChild(empty);
        }
    }

    /**
     * Shows a small spinner inside `.trackSelections`.
     *
     * @param {Element} page Details content root.
     */
    function showSpinner(page) {
        removeStatus(page);
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        var spin = document.createElement('div');
        spin.className = SPINNER_CLASS;
        spin.setAttribute('role', 'status');
        spin.setAttribute('aria-label', 'Loading streams');
        spin.style.cssText = [
            'width:1.4em',
            'height:1.4em',
            'border:2px solid rgba(255,255,255,0.2)',
            'border-top-color:rgba(255,255,255,0.8)',
            'border-radius:50%',
            'animation:tmdbsearch-spin 0.7s linear infinite',
            'margin:0.4em auto',
            'display:block',
            'flex-shrink:0'
        ].join(';');
        form.insertBefore(spin, form.firstChild);
        form.classList.remove('hide');
    }

    /**
     * Shows a non-intrusive empty state when only placeholder sources exist.
     *
     * @param {Element} page Details content root.
     */
    function showNoStreams(page) {
        removeStatus(page);
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        var msg = document.createElement('div');
        msg.className = EMPTY_CLASS;
        msg.style.cssText = 'color:rgba(255,255,255,0.5);font-size:0.85em;text-align:center;padding:0.4em 0;';
        msg.textContent = 'No streams available';
        form.insertBefore(msg, form.firstChild);
        form.classList.remove('hide');
    }

    /**
     * After a full getItem DTO, hint when streams are still placeholders.
     * Does not rewrite MediaSources or disable Play.
     *
     * @param {object} item Jellyfin item DTO from the original getItem.
     * @param {string} itemId Item GUID.
     */
    function maybeShowPlaceholderHint(item, itemId) {
        if (!needsAsyncStreams(item) || hasPlayableSources(item)) {
            return;
        }

        var type = item && item.Type;
        if (type !== 'Movie' && type !== 'Episode') {
            return;
        }

        function apply() {
            if (!isCurrentPage(itemId)) {
                return;
            }

            var page = getDetailsPage();
            if (!page) {
                setTimeout(apply, 50);
                return;
            }

            showSpinner(page);
            setTimeout(function () {
                if (!isCurrentPage(itemId)) {
                    return;
                }

                var current = getDetailsPage();
                if (current) {
                    showNoStreams(current);
                }
            }, 400);
        }

        apply();
    }

    /**
     * Wraps ApiClient.getItem without changing the request, then hints on stubs.
     *
     * @param {object} apiClient window.ApiClient instance.
     */
    function patchApiClientProto(apiClient) {
        var proto = Object.getPrototypeOf(apiClient);
        if (!proto || proto[PATCH_FLAG]) {
            return;
        }

        proto[PATCH_FLAG] = true;
        proto[ORIGINAL_FLAG] = proto.getItem;

        proto.getItem = function (userId, itemId) {
            var self = this;
            var original = proto[ORIGINAL_FLAG];
            var request = original.call(self, userId, itemId);
            if (typeof itemId !== 'string' || !isCurrentPage(itemId)) {
                return request;
            }

            return request.then(function (item) {
                if (!isCurrentPage(itemId)) {
                    return item;
                }

                maybeShowPlaceholderHint(item, itemId);
                return item;
            });
        };
    }

    injectCss();

    var realApiClient = null;
    try {
        Object.defineProperty(window, 'ApiClient', {
            configurable: true,
            get: function () {
                return realApiClient;
            },
            set: function (value) {
                realApiClient = value;
                if (value) {
                    patchApiClientProto(value);
                }
            }
        });
    } catch (error) {
        (function poll() {
            if (window.ApiClient) {
                patchApiClientProto(window.ApiClient);
            } else {
                setTimeout(poll, 50);
            }
        }());
    }
}());
