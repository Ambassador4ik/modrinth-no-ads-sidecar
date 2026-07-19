(function () {
	'use strict'

	var stateKey = '__modrinthNoAdsSidecarMainV1'
	var styleId = 'modrinth-no-ads-sidecar-style'

	function errorText(error) {
		try {
			return String((error && error.message) || error || 'unknown error').slice(0, 240)
		} catch (_) {
			return 'unknown error'
		}
	}

	function isVerifiedMainView(internals) {
		var metadata = internals && internals.metadata
		var webview = metadata && metadata.currentWebview
		var windowMetadata = metadata && metadata.currentWindow
		var labelsMatch =
			(!webview || webview.label === 'main') &&
			(!windowMetadata || windowMetadata.label === 'main')
		var originMatches = location.hostname === 'tauri.localhost' || location.protocol === 'tauri:'

		// Require independent Modrinth evidence in addition to generic Tauri labels.
		return (
			labelsMatch &&
			originMatches &&
			document.title === 'Modrinth App' &&
			!!document.querySelector('.app-viewport') &&
			!!document.querySelector('.app-sidebar')
		)
	}

	function installStyle() {
		if (!document.documentElement) {
			return false
		}

		var style = document.getElementById(styleId)
		if (!style) {
			style = document.createElement('style')
			style.id = styleId
			style.textContent =
				'.app-sidebar > .ad-parent,' +
				'.app-sidebar a[href^="https://modrinth.plus?app"],' +
				'.app-sidebar a[href^="https://modrinth.host/medal?from=app-placeholder"]{' +
				'display:none!important;' +
				'}' +
				'.app-sidebar::after{' +
				'content:none!important;' +
				'display:none!important;' +
				'}' +
				'.app-sidebar .app-sidebar-scrollable.pb-12{' +
				'padding-bottom:0!important;' +
				'}'
			document.documentElement.appendChild(style)
		}

		return !!document.getElementById(styleId)
	}

	function status(state) {
		return {
			kind: 'main',
			verified: state.verified,
			hideOk: state.hideOk,
			wrapperInstalled: state.wrapperInstalled,
			styleInstalled: state.styleInstalled,
			active: state.verified && state.hideOk && state.styleInstalled,
			error: state.lastError,
		}
	}

	function install() {
		if (window !== window.top) {
			return Promise.resolve({
				kind: 'main',
				verified: false,
				hideOk: false,
				wrapperInstalled: false,
				styleInstalled: false,
				active: false,
				error: 'not-top-frame',
			})
		}

		var existing = window[stateKey]
		if (existing && typeof existing.tick === 'function') {
			return existing.tick()
		}

		var state = {
			rawInvoke: null,
			timer: null,
			hideInFlight: null,
			verified: false,
			hideOk: false,
			wrapperInstalled: false,
			styleInstalled: false,
			lastError: null,
		}

		state.schedule = function () {
			window.clearTimeout(state.timer)
			state.timer = window.setTimeout(
				function () {
					Promise.resolve(state.tick()).then(state.schedule, state.schedule)
				},
				state.verified ? 250 : 50,
			)
		}

		state.tick = function () {
			var internals = window.__TAURI_INTERNALS__
			state.verified =
				!!internals &&
				typeof internals.invoke === 'function' &&
				isVerifiedMainView(internals)

			if (!state.verified) {
				state.hideOk = false
				state.wrapperInstalled = false
				state.lastError = 'main-view-not-ready'
				return Promise.resolve(status(state))
			}

			state.styleInstalled = installStyle()
			state.rawInvoke = internals.invoke.bind(internals)
			state.wrapperInstalled = false

			if (state.hideInFlight) {
				return state.hideInFlight.then(function () {
					return status(state)
				})
			}

			try {
				state.hideInFlight = Promise.resolve(
					state.rawInvoke('plugin:ads|hide_ads_window', { reset: true }),
				).then(
					function () {
						state.hideOk = true
						state.lastError = null
					},
					function (error) {
						state.hideOk = false
						state.lastError = 'native-hide: ' + errorText(error)
					},
				)
			} catch (error) {
				state.hideOk = false
				state.lastError = 'native-hide: ' + errorText(error)
				return Promise.resolve(status(state))
			}

			return state.hideInFlight.then(function () {
				state.hideInFlight = null
				return status(state)
			})
		}

		window[stateKey] = state
		return state.tick().then(function (result) {
			state.schedule()
			return result
		})
	}

	return install()
})()
