ALTER TABLE transactions."TransactionCategories"
  ADD COLUMN "ParentCategoryId" integer NULL
  REFERENCES transactions."TransactionCategories"("Id") ON DELETE RESTRICT;
