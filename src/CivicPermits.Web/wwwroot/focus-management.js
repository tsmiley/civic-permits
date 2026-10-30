// Where keyboard and screen-reader focus goes after the page changes.
//
// A full page load starts at the top of the document, so the first Tab reaches the skip link: nothing is moved.
// The exception is a form that came back with problems: focus goes to the error summary, so a screen reader says
// what is wrong first and a keyboard user is next to the links to the fields.
//
// Blazor's enhanced navigation swaps the page without a load. After one, focus moves to the new page's summary or
// its h1, so a screen reader announces where the person now is.
function moveFocus(preferHeading) {
  const summary = document.querySelector('[data-error-summary]');
  if (summary) {
    summary.focus();
    return;
  }

  if (preferHeading) {
    const heading = document.querySelector('h1');
    if (heading) {
      heading.setAttribute('tabindex', '-1');
      heading.focus();
    }
  }
}

// A link in the error summary goes to its field and puts focus there. Blazor handles same-page links itself and would
// scroll without moving focus, so this does it, as the skip link does.
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('[data-error-summary] a') : null;
  if (!link || !link.hash) {
    return;
  }

  const field = document.getElementById(decodeURIComponent(link.hash.slice(1)));
  if (!field) {
    return;
  }

  event.preventDefault();
  field.focus();
  field.scrollIntoView({ block: 'center' });
});

document.addEventListener('DOMContentLoaded', () => moveFocus(false));

if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
  window.Blazor.addEventListener('enhancednavigationend', () => moveFocus(true));
}
