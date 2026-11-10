// Included only in the UI test build, never the release build.
import '@fontsource/pixelify-sans/latin-400.css';
import '@fontsource/atkinson-hyperlegible/latin-400.css';
import '@fontsource/atkinson-hyperlegible/latin-700.css';
import '../../../src/ui/styles.css';
import '../../../src/ui/shell.css';
import '../../../src/ui/presentation.css';
import { renderResults } from '../../../src/ui/results';
import { setVolume } from '../../../src/audio/sound';
import statementText from '../../fixtures/ui_statement.json?raw';
import objectivesText from '../../fixtures/ui_objectives.json?raw';
import type { Views } from '../../../src/sim/types';
import { applyLanguage, t } from '../../../src/i18n';
import { dialogs } from '../../../src/ui/dialog';

setVolume(0);
applyLanguage();
renderResults(document.getElementById('app')!, 'First Venture', JSON.parse(statementText) as NonNullable<Views['results']>,
  JSON.parse(objectivesText) as Views['objectives'], {
    next: () => { location.href = new URL('../../../index.html', location.href).href; },
    menu: () => { location.href = new URL('../../../index.html', location.href).href; },
  }, 1000);

if (new URLSearchParams(location.search).get('screen') === 'news') {
  void dialogs(document.body, () => document.querySelector('main')).show([{ title: t('scenario.news'), text:
    'Robbers are waiting on the road from k0n0r0 to k0n0r0c1. Guards reduce the attack chance by 70%. Insurance refunds 80% of lost cargo value. Choose your protection before you leave.',
  }]);
}
