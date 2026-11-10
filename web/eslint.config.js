import js from '@eslint/js';
import globals from 'globals';
import sonarjs from 'eslint-plugin-sonarjs';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: [
      'dist',
      'dist-e2e',
      'dist-ui',
      'node_modules',
      'public',
      'bench',
      // Generated WebAssembly bindings are not hand-written code. src/i18n needs no exclusion: it passes the limits.
      'src/sim/pkg',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  { languageOptions: { globals: { ...globals.browser, ...globals.node } } },
  {
    plugins: { sonarjs },
    rules: {
      'complexity': ['error', 25],
      'max-depth': ['error', 3],
      'max-lines': ['error', { max: 1000, skipBlankLines: true, skipComments: true }],
      'max-lines-per-function': ['error', { max: 80, skipBlankLines: true, skipComments: true }],
      'max-params': ['error', 7],
      'sonarjs/cognitive-complexity': ['error', 15],
    },
  },
  {
    files: ['**/*.{test,spec}.{ts,tsx}', '**/__tests__/**/*.{ts,tsx}'],
    rules: {
      // describe blocks make test functions intentionally longer than production functions.
      'max-lines-per-function': ['error', { max: 160, skipBlankLines: true, skipComments: true }],
    },
  },
);
