import { scenarioMessages } from './index';
import { createScenarioTranslator } from './scenario-translator';

// The chosen language is loaded once before dependent UI modules initialize.
export const translateScenario = createScenarioTranslator(scenarioMessages);
