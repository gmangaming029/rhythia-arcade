export type Chart = {
  chartId: string;
  title: string;
  creator: string;
  description: string;
  difficulty: string;
  tags: string[];
  versionId: string;
  sizeBytes: number;
  sha256: string;
  updatedAt: string;
  rankedState: "unranked" | "ranked";
};

export type Page<T> = {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
};

export type ChartSearch = {
  q?: string;
  creator?: string;
  difficulty?: string;
  tag?: string[];
  sort?: "updated" | "popular";
  page?: number;
  pageSize?: number;
};

export type UserProfile = { name?: string; email?: string; subject: string };
