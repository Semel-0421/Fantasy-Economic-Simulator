export interface NameData {
  code: string;
  curatedPlaces: Record<string, string>;
  curatedGoods: Record<string, string>;
  starts: string[];
  middles: string[];
  ends: string[];
  landforms: string[];
  qualifiers: string[];
  categoryNouns: string[][];
  banned: RegExp;
  patterns: {
    goods(qualifier: string, noun: string): string;
    fallback(qualifier: string): string;
    place(name: string, landform: string): string;
  };
}
