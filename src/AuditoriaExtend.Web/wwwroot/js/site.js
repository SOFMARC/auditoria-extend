// Atualiza um parâmetro na URL e retorna a nova URL
function updateQueryParam(url, key, value, ...resetKeys) {
    const u = new URL(url, window.location.origin);
    u.searchParams.set(key, value);
    for (let i = 0; i < resetKeys.length; i += 2) {
        if (resetKeys[i + 1] !== undefined) {
            u.searchParams.set(resetKeys[i], resetKeys[i + 1]);
        }
    }
    return u.toString();
}

// Ordena tabela pelo cabeçalho clicado
function sortTable(col, currentSortBy, currentSortOrder) {
    const newOrder = (currentSortBy === col && currentSortOrder === 'asc') ? 'desc' : 'asc';
    const url = updateQueryParam(window.location.href, 'sortBy', col, 'sortOrder', newOrder, 'page', '1');
    window.location.href = url;
}

// Auto-dismiss alerts após 5 segundos
document.addEventListener('DOMContentLoaded', function () {
    setTimeout(function () {
        document.querySelectorAll('.alert-dismissible').forEach(function (el) {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(el);
            bsAlert.close();
        });
    }, 5000);
});
