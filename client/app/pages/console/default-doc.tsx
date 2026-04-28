import { Link } from "react-router-dom";

type DocCardProps = {
  label: string;
  imageUri: string;
  description: string;
  url: string;
};

const DocCard = ({ label, imageUri, description, url }: DocCardProps) => {
  return (
    <a href={url} target="_blank" rel="noopener noreferrer">
      <div className="flex flex-col gap-3">
        <h4 className="m-0 block text-xl font-semibold sm:hidden">{label}</h4>
        <div className="flex max-w-2xl items-center justify-center rounded border bg-card">
          <div className="my-8 text-center">
            <img src={imageUri} width={188} height={188} alt={label} />
          </div>
        </div>
        <h4 className="m-0 hidden text-xl font-semibold sm:block">{label}</h4>
        <div className="text-base font-normal text-high-emphasis">{description}</div>
      </div>
    </a>
  );
};

const data = [
  {
    label: "Docs",
    description:
      "Established standards that help project managers and technical leaders minimize project risks.",
    imageUri: "/assets/images/console/console_timeline.png",
    url: "https://github.com/SELISEdigitalplatforms/Wiki-BlocksGuideline-Code/wiki",
  },
  {
    label: "Code",
    description:
      "A repository of well-documented, reusable, tried and tested core components for developers.",
    imageUri: "/assets/images/console/console_coding.png",
    url: "https://github.com/SELISEdigitalplatforms",
  },
  {
    label: "Cloud",
    description: "High-performing, optimized, and 24/7 monitored enterprise cloud deployment.",
    imageUri: "/assets/images/console/console_data-center.png",
    url: "https://selisegroup.com/blocks/",
  },
];

export const DefaultDoc = () => {
  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {data.map((item, index) => (
        <DocCard key={index} {...item} />
      ))}
    </div>
  );
};
