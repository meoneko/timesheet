// Reply / edit toggle behavior for the comment thread partial (_CommentList.cshtml).
// Uses event delegation on document so it keeps working after the list is
// reloaded via AJAX (e.g. "Load more" / after posting a comment).
(function () {
    document.addEventListener('click', function (e) {
        var replyToggle = e.target.closest('.reply-toggle');
        if (replyToggle) {
            var id = replyToggle.dataset.commentId;
            var form = document.getElementById('replyForm-' + id);
            if (form) {
                form.style.display = form.style.display === 'none' ? 'block' : 'none';
                var textarea = form.querySelector('textarea');
                if (textarea && form.style.display === 'block') textarea.focus();
            }
            return;
        }

        var replyCancel = e.target.closest('.reply-cancel');
        if (replyCancel) {
            var rid = replyCancel.dataset.commentId;
            var rform = document.getElementById('replyForm-' + rid);
            if (rform) rform.style.display = 'none';
            return;
        }

        var editToggle = e.target.closest('.edit-toggle');
        if (editToggle) {
            var eid = editToggle.dataset.commentId;
            var editForm = document.getElementById('editForm-' + eid);
            var content = document.getElementById('commentContent-' + eid);
            if (editForm && content) {
                var showing = editForm.style.display !== 'none';
                editForm.style.display = showing ? 'none' : 'block';
                content.style.display = showing ? '' : 'none';
                var ta = editForm.querySelector('textarea');
                if (ta && !showing) ta.focus();
            }
            return;
        }

        var editCancel = e.target.closest('.edit-cancel');
        if (editCancel) {
            var cid = editCancel.dataset.commentId;
            var cEditForm = document.getElementById('editForm-' + cid);
            var cContent = document.getElementById('commentContent-' + cid);
            if (cEditForm) cEditForm.style.display = 'none';
            if (cContent) cContent.style.display = '';
            return;
        }

        var loadMore = e.target.closest('.load-more-comments');
        if (loadMore) {
            var entityType = loadMore.dataset.entityType;
            var entityId = loadMore.dataset.entityId;
            var nextPage = loadMore.dataset.nextPage;
            var url = '/Comments/List?entityType=' + encodeURIComponent(entityType) +
                '&entityId=' + encodeURIComponent(entityId) + '&page=' + encodeURIComponent(nextPage);
            loadMore.disabled = true;
            fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (resp) { return resp.text(); })
                .then(function (html) {
                    var parser = new DOMParser();
                    var doc = parser.parseFromString(html, 'text/html');
                    var newItems = doc.querySelectorAll('#commentList .comment-item');
                    var list = document.getElementById('commentList');
                    if (list && newItems.length) {
                        newItems.forEach(function (item) { list.appendChild(item); });
                        loadMore.dataset.nextPage = (parseInt(nextPage, 10) + 1).toString();
                        loadMore.disabled = false;
                    } else {
                        loadMore.remove();
                    }
                })
                .catch(function () { loadMore.disabled = false; });
        }
    });
})();
