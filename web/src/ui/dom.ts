// Small DOM helpers shared by the menu and the HUD.
import { displayText } from '../data/names';
import { translateScenario } from '../i18n/scenario';

const playerText = (text: string): string => displayText(translateScenario(text));
export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Partial<{ id: string; className: string; text: string; type: string; title: string }> = {},
  ...children: (Node | string)[]
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (props.id) node.id = props.id;
  if (props.className) node.className = props.className;
  if (props.text !== undefined) node.textContent = playerText(props.text);
  if (props.type) node.setAttribute('type', props.type);
  if (props.title) node.title = playerText(props.title);
  node.append(...children.map((child) => typeof child === 'string' ? playerText(child) : child));
  return node;
}

export const button = (text: string, onClick: () => void, id?: string): HTMLButtonElement => {
  const b = el('button', { text, type: 'button', id });
  b.addEventListener('click', onClick);
  return b;
};
