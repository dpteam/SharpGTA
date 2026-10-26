// SharpGTA launcher. ES5 only for the system MSHTML/Trident engine.
// Host bridge: pending payload + cancelled sharpgta:// navigation; no COM external object.
'use strict';
var SharpGTA = (function () {
    var $ = function (id) { return document.getElementById(id); };
    var game = 'iii';
    var theme = 'dark';
    var timer = null;
    var running = false;

    function post(verb, payload) {
        try {
            window.__sharpGtaOutbox = payload === undefined ? '' : String(payload);
            window.location.href = 'sharpgta://' + verb;
            return true;
        } catch (e) {
            return false;
        }
    }

    window.sharpGtaTakePayload = function () {
        var payload = window.__sharpGtaOutbox || '';
        window.__sharpGtaOutbox = '';
        return payload;
    };

    var preserved = ['128', '1.2', '64', '64', null, null, '1', '0', '0', null, '0.005', '0'];

    function collect() {
        return [
            game,
            $('root').value,
            theme,
            $('language').value,
            preserved[0], preserved[1], preserved[2], preserved[3],
            $('frameLimit').checked ? '1' : '0',
            $('vsync').checked ? '1' : '0',
            preserved[6], preserved[7], preserved[8],
            $('resolution').value,
            preserved[10], preserved[11]
        ].join('\n');
    }

    function apply(payload) {
        if (!payload) { return; }
        var f = String(payload).split('\n');
        game = f[0] === 'vc' ? 'vc' : 'iii';
        $('root').value = f[1] || '';
        // State layout: 0 game, 1 root, 2 theme, 3 language, 4 brightness,
        // 5 draw distance, 6 music, 7 sfx, 8 frame limit, 9 vsync,
        // 10 trails, 11 subtitles, 12 widescreen, 13 resolution, 14 mouse, 15 invert.
        theme = f[2] === 'light' ? 'light' : 'dark';
        $('language').value = f[3] || '0';
        preserved = [f[4] || '128', f[5] || '1.2', f[6] || '64', f[7] || '64',
            null, null, f[10] || '1', f[11] || '0', f[12] || '0', null,
            f[14] || '0.005', f[15] || '0'];
        $('frameLimit').checked = f[8] !== '0';
        $('vsync').checked = f[9] !== '0';
        $('resolution').value = f[13] || '0';
        setTheme(theme);
        syncGame();
        requestPreview();
    }

    function syncGame() {
        $('gameIii').className = game === 'iii' ? 'selected' : '';
        $('gameVc').className = game === 'vc' ? 'selected' : '';
        $('profileHint').innerHTML = game === 'iii'
            ? 'GTA III: интерактивное меню на оригинальных MENU.TXD, FONTS.TXD и GXT.'
            : 'Vice City: DAT/DFF/TXD/COLL/IMG уже читаются; интерактивный frontend ещё впереди.';
        $('vcWarning').className = game === 'vc' ? 'hint warn' : 'hint warn hidden';
        $('launch').innerHTML = game === 'iii'
            ? 'Запустить frontend &nbsp;&#8599;'
            : 'Проверить запуск VC &nbsp;&#8599;';
    }

    function setTheme(value) {
        theme = value === 'light' ? 'light' : 'dark';
        document.documentElement.className = theme === 'dark' ? 'dark' : '';
        $('themeToggle').innerHTML = theme === 'dark' ? 'Светлая тема' : 'Тёмная тема';
    }

    function setStatus(text, isError) {
        var node = $('status');
        node.className = isError ? 'status error' : 'status';
        node.innerHTML = '';
        node.appendChild(document.createTextNode(text || ''));
    }

    function setRunning(value) {
        running = value;
        $('launch').disabled = value;
        if (value) {
            $('launch').innerHTML = 'Frontend запущен &nbsp;&#8599;';
        } else {
            syncGame();
        }
    }

    function requestPreview() {
        if (timer) { clearTimeout(timer); }
        timer = setTimeout(function () {
            timer = null;
            post('preview', collect());
        }, 120);
    }

    window.sharpGtaReceive = function (verb, payload) {
        window.__sharpGtaHostSeen = true;
        if (verb === 'state') {
            apply(payload);
        } else if (verb === 'preview') {
            $('preview').innerHTML = '';
            $('preview').appendChild(document.createTextNode(payload || '—'));
        } else if (verb === 'status') {
            var lines = String(payload || '').split('\n');
            setStatus(lines[0], lines[1] === '1');
        } else if (verb === 'theme') {
            setTheme(payload);
        } else if (verb === 'running') {
            setRunning(String(payload) === '1');
        } else if (verb === 'storage') {
            $('storage').innerHTML = '';
            $('storage').appendChild(document.createTextNode(String(payload || '')));
        }
    };

    function chooseGame(value) {
        game = value;
        syncGame();
        requestPreview();
    }

    function init() {
        var ids = ['root', 'language', 'resolution'];
        var i;
        for (i = 0; i < ids.length; i++) {
            $(ids[i]).onchange = requestPreview;
            $(ids[i]).onkeyup = requestPreview;
        }
        $('vsync').onclick = requestPreview;
        $('frameLimit').onclick = requestPreview;
        $('gameIii').onclick = function () { chooseGame('iii'); };
        $('gameVc').onclick = function () { chooseGame('vc'); };
        $('themeToggle').onclick = function () {
            setTheme(theme === 'dark' ? 'light' : 'dark');
            requestPreview();
        };
        $('browseRoot').onclick = function () { post('browse', 'root\n' + collect()); };
        $('save').onclick = function () { post('save', collect()); };
        $('launch').onclick = function () {
            if (running) { return; }
            setStatus('Запуск…', false);
            post('launch', collect());
        };
        setTheme(theme);
        syncGame();
        post('ready', '');
        setTimeout(function () {
            if (!window.__sharpGtaHostSeen) {
                setStatus('Предпросмотр дизайна. Запуск доступен только внутри EXE.', true);
                $('launch').disabled = true;
            }
        }, 1500);
    }

    return { init: init, collect: collect, apply: apply, setTheme: setTheme };
})();
SharpGTA.init();
