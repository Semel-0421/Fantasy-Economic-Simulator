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

setVolume(0);
renderResults(document.getElementById('app')!, 'First Venture', JSON.parse(statementText) as NonNullable<Views['results']>,
  JSON.parse(objectivesText) as Views['objectives'], {
    next: () => { location.href = new URL('../../../index.html', location.href).href; },
    menu: () => { location.href = new URL('../../../index.html', location.href).href; },
  }, 1000);
