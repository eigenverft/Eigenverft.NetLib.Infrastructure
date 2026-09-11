(function () {
  'use strict';

  var loader = document.currentScript;
  var loaderSource = loader && loader.src ? loader.src : '';
  var scriptRoot = loaderSource ? loaderSource.substring(0, loaderSource.lastIndexOf('/') + 1) : './documentation/js/';
  var scripts = [
    'documentation.pages.js',
    '../../vendor/js/bootstrap.bundle.min.js',
    '../../vendor/js/marked.umd.js',
    '../../vendor/js/highlight.min.js',
    '../../vendor/js/highlight.powershell.min.js',
    '../../vendor/js/highlight.dos.min.js',
    '../../vendor/js/clipboard.min.js',
    '../../vendor/js/mermaid.min.js',
    'documentation.js'
  ];

  function showLoadError(fileName) {
    var message = document.createElement('p');
    message.className = 'page-width documentation-content warning-panel';
    message.textContent = 'Packaged documentation dependency could not be loaded: ' + fileName;
    document.body.insertBefore(message, document.body.firstChild);
  }

  function loadScript(index) {
    if (index >= scripts.length) {
      return;
    }

    var fileName = scripts[index];
    var script = document.createElement('script');
    script.src = scriptRoot + fileName;
    script.async = false;
    script.onload = function () {
      loadScript(index + 1);
    };
    script.onerror = function () {
      showLoadError(fileName);
    };
    document.head.appendChild(script);
  }

  loadScript(0);
}());
